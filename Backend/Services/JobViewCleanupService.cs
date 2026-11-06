using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

// Usuwa w tle wpisy deduplikacji wyświetleń (JobOfferViewers) starsze niż dzień wstecz - liczy się tylko bieżący
// dzień UTC, wczorajszy zostaje jako zapas na przełom doby. Agregaty dzienne zostają bez zmian.
public class JobViewCleanupService : BackgroundService
{
    private const int BatchSize = 5000;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobViewCleanupService> _logger;

    public JobViewCleanupService(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<JobViewCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await CleanUpAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // np. baza jeszcze niegotowa - kolejna próba za godzinę
                _logger.LogWarning(ex, "Failed to clean up the job view deduplication records");
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    // Usuwa partiami (krótkie blokady); zwraca liczbę usuniętych wpisów
    public async Task<int> CleanUpAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime).AddDays(-1);
        var total = 0;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        int deleted;
        do
        {
            deleted = await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM `JobOfferViewers` WHERE `Date` < {cutoff} LIMIT {BatchSize}", cancellationToken);
            total += deleted;
        }
        while (deleted == BatchSize);

        if (total > 0)
        {
            _logger.LogInformation("Removed {Count} job view deduplication records older than {Cutoff}", total, cutoff);
        }

        return total;
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
