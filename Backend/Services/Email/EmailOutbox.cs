using Backend.Data;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Services.Email;

public sealed class EmailOutbox : IEmailOutbox
{
    // najwyżej tyle e-maili resetu hasła na konto w ciągu godziny (niezależnie od limitu żądań per IP) - ochrona
    // skrzynki przed zalewem wiadomości z wielu adresów
    public const int MaxPasswordResetsPerHour = 3;

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EmailTemplates _templates;
    private readonly TimeProvider _time;
    private readonly TimeSpan _confirmationLifespan;
    private readonly TimeSpan _resetLifespan;

    public EmailOutbox(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        EmailTemplates templates,
        TimeProvider time,
        IOptions<EmailConfirmationTokenProviderOptions> confirmationTokens,
        IOptions<DataProtectionTokenProviderOptions> resetTokens)
    {
        _context = context;
        _userManager = userManager;
        _templates = templates;
        _time = time;
        _confirmationLifespan = confirmationTokens.Value.TokenLifespan;
        _resetLifespan = resetTokens.Value.TokenLifespan;
    }

    public async Task QueueEmailConfirmationAsync(ApplicationUser user)
    {
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var content = _templates.EmailConfirmation(user.FirstName, user.Id, token, _confirmationLifespan);
        Queue([Create(user.Id, user.Email!, FullName(user.FirstName, user.LastName), EmailType.EmailConfirmation, content)]);
    }

    public async Task<bool> QueuePasswordResetAsync(ApplicationUser user)
    {
        var since = Now.AddHours(-1);
        var recent = await _context.EmailMessages.CountAsync(m =>
            m.UserId == user.Id && m.Type == EmailType.PasswordReset && m.CreatedAt >= since);
        if (recent >= MaxPasswordResetsPerHour)
        {
            return false;
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var content = _templates.PasswordReset(user.FirstName, user.Email!, token, _resetLifespan);
        Queue([Create(user.Id, user.Email!, FullName(user.FirstName, user.LastName), EmailType.PasswordReset, content)]);
        return true;
    }

    public async Task<IReadOnlyList<EmailMessage>> PrepareApplicationSubmittedAsync(int jobOfferId, string candidateId)
    {
        var job = await JobSummaryAsync(jobOfferId);
        if (job == null)
        {
            return [];
        }

        var messages = new List<EmailMessage>();

        var candidate = await Recipients(u => u.Id == candidateId && u.NotifyApplicationStatusChanged).FirstOrDefaultAsync();
        if (candidate != null)
        {
            messages.Add(Create(candidate, EmailType.ApplicationSubmitted,
                _templates.ApplicationSubmitted(candidate.FirstName, job.Title, job.CompanyName)));
        }

        var members = await Recipients(u => u.CompanyId == job.CompanyId && u.NotifyNewApplication).ToListAsync();
        messages.AddRange(members.Select(member =>
            Create(member, EmailType.NewApplication, _templates.NewApplication(member.FirstName, job.Title))));

        return messages;
    }

    public async Task<IReadOnlyList<EmailMessage>> PrepareApplicationStatusChangedAsync(int jobOfferId, string candidateId,
        ApplicationStatus status, string? comment)
    {
        var candidate = await Recipients(u => u.Id == candidateId && u.NotifyApplicationStatusChanged).FirstOrDefaultAsync();
        var job = candidate == null ? null : await JobSummaryAsync(jobOfferId);
        if (candidate == null || job == null)
        {
            return [];
        }

        return
        [
            Create(candidate, EmailType.ApplicationStatusChanged,
                _templates.ApplicationStatusChanged(candidate.FirstName, job.Title, job.CompanyName, status, comment))
        ];
    }

    public async Task<IReadOnlyList<EmailMessage>> PrepareCompanyReviewedAsync(Company company)
    {
        if (company.Status is not (CompanyStatus.Approved or CompanyStatus.Rejected))
        {
            return [];
        }

        var approved = company.Status == CompanyStatus.Approved;
        var members = await Recipients(u => u.CompanyId == company.Id && u.NotifyCompanyReview).ToListAsync();
        return members
            .Select(member => Create(member, EmailType.CompanyReviewed,
                _templates.CompanyReviewed(member.FirstName, company.Name, approved, company.RejectionReason)))
            .ToList();
    }

    public void Queue(IEnumerable<EmailMessage> messages) => _context.EmailMessages.AddRange(messages);

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private IQueryable<Recipient> Recipients(System.Linq.Expressions.Expression<Func<ApplicationUser, bool>> filter) =>
        _context.Users
            .AsNoTracking()
            .Where(filter)
            .Where(u => u.Email != null)
            .Select(u => new Recipient(u.Id, u.Email!, u.FirstName, u.LastName));

    private Task<JobSummary?> JobSummaryAsync(int jobOfferId) =>
        _context.JobOffers
            .AsNoTracking()
            .Where(j => j.Id == jobOfferId)
            .Select(j => new JobSummary(j.Title, j.CompanyId, j.Company.Name))
            .FirstOrDefaultAsync();

    private EmailMessage Create(Recipient recipient, EmailType type, EmailContent content) =>
        Create(recipient.Id, recipient.Email, FullName(recipient.FirstName, recipient.LastName), type, content);

    private EmailMessage Create(string userId, string address, string? name, EmailType type, EmailContent content)
    {
        var now = Now;
        return new EmailMessage
        {
            UserId = userId,
            Type = type,
            ToAddress = address,
            ToName = name,
            Subject = content.Subject,
            HtmlBody = content.HtmlBody,
            TextBody = content.TextBody,
            Status = EmailMessageStatus.Pending,
            CreatedAt = now,
            NextAttemptAt = now
        };
    }

    private static string? FullName(string? firstName, string? lastName)
    {
        var name = $"{firstName} {lastName}".Trim();
        return name.Length == 0 ? null : name[..Math.Min(name.Length, 200)];
    }

    private sealed record Recipient(string Id, string Email, string? FirstName, string? LastName);

    private sealed record JobSummary(string Title, int CompanyId, string CompanyName);
}
