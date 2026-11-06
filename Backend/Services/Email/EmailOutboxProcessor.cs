using Backend.Data;
using Backend.Interfaces;
using Backend.Models;
using Backend.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Services.Email;

// Wysyła wiadomości z outboksu (tabela EmailMessages) w tle: po sygnale nowej wiadomości (EmailOutboxSignal) albo co
// PollInterval. Nieudana próba - kolejna po rosnącym opóźnieniu (backoff), po MaxAttempts próbach albo trwałym
// odrzuceniu przez serwer - status Failed. Po wysłaniu i ostatecznym błędzie treść jest czyszczona (linki z tokenami
// nie zostają w bazie), a stare wpisy są usuwane po RetentionDays. Jedna instancja aplikacji (projekt lokalny) -
// wiadomości nie są rezerwowane; przerwanie w trakcie wysyłki może najwyżej wysłać wiadomość ponownie.
public sealed class EmailOutboxProcessor : BackgroundService
{
    public const int MaxErrorLength = 500;

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmailSender _sender;
    private readonly EmailOutboxSignal _signal;
    private readonly EmailSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<EmailOutboxProcessor> _logger;
    private DateTime _nextCleanupAt = DateTime.MinValue;

    public EmailOutboxProcessor(
        IServiceScopeFactory scopeFactory,
        IEmailSender sender,
        EmailOutboxSignal signal,
        IOptions<EmailSettings> settings,
        TimeProvider time,
        ILogger<EmailOutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _sender = sender;
        _signal = signal;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
    }

    private EmailOutboxSettings Outbox => _settings.Outbox;

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.IsSmtpConfigured)
        {
            _logger.LogWarning("Email:Smtp:Host is not configured - e-mails are kept in the outbox and not sent");
            return;
        }

        // start hosta nie czeka na pierwszy cykl
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            var batchFull = false;
            try
            {
                batchFull = await SendDueMessagesAsync(stoppingToken) >= Outbox.BatchSize;
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // np. baza chwilowo niedostępna - kolejna próba w następnym cyklu
                _logger.LogError(ex, "E-mail outbox processing failed");
            }

            if (batchFull)
            {
                continue;
            }

            try
            {
                await _signal.WaitAsync(Outbox.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // liczba wiadomości pobranych do wysłania w tym cyklu
    private async Task<int> SendDueMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = Now;
        var due = await context.EmailMessages
            .AsNoTracking()
            .Where(m => m.Status == EmailMessageStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt).ThenBy(m => m.Id)
            .Take(Outbox.BatchSize)
            .ToListAsync(stoppingToken);

        foreach (var message in due)
        {
            stoppingToken.ThrowIfCancellationRequested();
            await SendAsync(context, message, stoppingToken);
        }

        return due.Count;
    }

    private async Task SendAsync(ApplicationDbContext context, EmailMessage message, CancellationToken stoppingToken)
    {
        if (message.HtmlBody == null || message.TextBody == null)
        {
            // obronnie: bez treści nie ma czego wysłać
            await FailAsync(context, message.Id, message.Attempts, "Message has no content");
            return;
        }

        var attempt = message.Attempts + 1;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
        {
            timeout.CancelAfter(Outbox.SendTimeout);
            try
            {
                await _sender.SendAsync(
                    new OutgoingEmail(message.ToAddress, message.ToName, message.Subject, message.HtmlBody, message.TextBody),
                    timeout.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // zamykanie aplikacji - próba nie jest liczona, wiadomość zostaje do wysłania po starcie
                throw;
            }
            catch (Exception ex)
            {
                var error = ex is OperationCanceledException
                    ? $"Timeout - sending took longer than {Outbox.SendTimeoutSeconds} s"
                    : Describe(ex);

                if (ex is EmailRejectedException || attempt >= Outbox.MaxAttempts)
                {
                    _logger.LogError("E-mail {EmailId} ({EmailType}) could not be sent after {Attempts} attempt(s) - giving up: {Error}",
                        message.Id, message.Type, attempt, error);
                    await FailAsync(context, message.Id, attempt, error);
                    return;
                }

                var nextAttemptAt = Now + Outbox.RetryDelay(attempt);
                _logger.LogWarning("E-mail {EmailId} ({EmailType}) could not be sent (attempt {Attempt} of {MaxAttempts}), " +
                                   "next attempt at {NextAttemptAt:O}: {Error}",
                    message.Id, message.Type, attempt, Outbox.MaxAttempts, nextAttemptAt, error);

                // bez tokenu zamykania - wynik próby musi zostać zapisany
                await context.EmailMessages
                    .Where(m => m.Id == message.Id && m.Status == EmailMessageStatus.Pending)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.Attempts, attempt)
                        .SetProperty(m => m.NextAttemptAt, nextAttemptAt)
                        .SetProperty(m => m.LastError, error), CancellationToken.None);
                return;
            }
        }

        var sentAt = Now;
        await context.EmailMessages
            .Where(m => m.Id == message.Id && m.Status == EmailMessageStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, EmailMessageStatus.Sent)
                .SetProperty(m => m.Attempts, attempt)
                .SetProperty(m => m.SentAt, sentAt)
                .SetProperty(m => m.LastError, (string?)null)
                .SetProperty(m => m.HtmlBody, (string?)null)
                .SetProperty(m => m.TextBody, (string?)null), CancellationToken.None);

        _logger.LogInformation("E-mail {EmailId} ({EmailType}) sent", message.Id, message.Type);
    }

    private static Task FailAsync(ApplicationDbContext context, long id, int attempts, string error) =>
        context.EmailMessages
            .Where(m => m.Id == id && m.Status == EmailMessageStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, EmailMessageStatus.Failed)
                .SetProperty(m => m.Attempts, attempts)
                .SetProperty(m => m.LastError, error)
                .SetProperty(m => m.HtmlBody, (string?)null)
                .SetProperty(m => m.TextBody, (string?)null), CancellationToken.None);

    // usuwa wysłane i nieudane wiadomości starsze niż RetentionDays (najwyżej raz na godzinę)
    private async Task CleanupAsync(CancellationToken stoppingToken)
    {
        var now = Now;
        if (now < _nextCleanupAt)
        {
            return;
        }

        _nextCleanupAt = now + CleanupInterval;
        var olderThan = now - Outbox.Retention;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deleted = await context.EmailMessages
            .Where(m => m.Status != EmailMessageStatus.Pending && m.CreatedAt < olderThan)
            .ExecuteDeleteAsync(stoppingToken);

        if (deleted > 0)
        {
            _logger.LogInformation("Deleted {Count} e-mail(s) older than {Days} days from the outbox", deleted, Outbox.RetentionDays);
        }
    }

    // typ i komunikat błędu (z wyjątkami wewnętrznymi), najwyżej 500 znaków; MailKit nie umieszcza w komunikatach
    // danych logowania SMTP
    public static string Describe(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current != null && parts.Count < 3; current = current.InnerException)
        {
            parts.Add($"{current.GetType().Name}: {current.Message}");
        }

        var text = string.Join(" -> ", parts).ReplaceLineEndings(" ");
        return text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
    }
}
