using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

public class JobApplication
{
    // statusy, z których kandydat może wycofać aplikację
    public static readonly IReadOnlySet<ApplicationStatus> WithdrawableStatuses = new HashSet<ApplicationStatus>
    {
        ApplicationStatus.Submitted,
        ApplicationStatus.InReview,
        ApplicationStatus.Interview,
        ApplicationStatus.Offered
    };

    public int Id { get; set; }

    [Required]
    public int JobOfferId { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Note { get; set; }

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Submitted;

    // token współbieżności - zmiana statusu albo notatka zapisywana na nieaktualnym stanie (aplikację zmieniło
    // w międzyczasie inne żądanie) kończy się DbUpdateConcurrencyException; notatka tokenu nie zmienia
    public DateTime StatusChangedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public JobOffer JobOffer { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
    public ICollection<ApplicationStatusHistory> StatusHistory { get; set; } = new List<ApplicationStatusHistory>();
    public ICollection<ApplicationAttachment> Attachments { get; set; } = new List<ApplicationAttachment>();

    // statusy, w których kandydat może dodawać załączniki (te same co przy wycofaniu); usuwać może w każdym statusie
    public static readonly IReadOnlySet<ApplicationStatus> AttachmentUploadStatuses = WithdrawableStatuses;

    public bool CanBeWithdrawn => WithdrawableStatuses.Contains(Status);

    public bool CanAddAttachments => AttachmentUploadStatuses.Contains(Status);

    // Nowa aplikacja ze statusem Submitted i pierwszym wpisem w historii (autor = kandydat)
    public static JobApplication Submit(int jobOfferId, string userId, string? note, DateTime now)
    {
        var application = new JobApplication
        {
            JobOfferId = jobOfferId,
            UserId = userId,
            Note = note,
            AppliedAt = now,
            StatusChangedAt = now
        };
        application.ChangeStatus(ApplicationStatus.Submitted, userId, null, now);
        return application;
    }

    // Zmienia status (albo dodaje notatkę, gdy status się nie zmienia) i dopisuje wpis do historii -
    // wpis zapisuje się w tym samym SaveChanges co aplikacja
    public ApplicationStatusHistory ChangeStatus(ApplicationStatus status, string? changedByUserId, string? comment, DateTime now)
    {
        if (status != Status)
        {
            Status = status;
            StatusChangedAt = now;
        }

        var entry = new ApplicationStatusHistory
        {
            Status = status,
            ChangedAt = now,
            ChangedByUserId = changedByUserId,
            Comment = comment
        };
        StatusHistory.Add(entry);
        return entry;
    }
}
