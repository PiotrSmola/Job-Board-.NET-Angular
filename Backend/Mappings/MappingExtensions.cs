using Backend.DTOs;
using Backend.Helpers;
using Backend.Models;

namespace Backend.Mappings;

// Ręczne mapowanie encji na DTO (jedno miejsce zamiast kopii w kontrolerach)
public static class MappingExtensions
{
    public static JobOfferDto ToDto(this JobOffer job) => job.ToDto(DateTime.UtcNow);

    // Wymaga załadowanej nawigacji Company (Include) - inaczej nazwa firmy będzie pusta
    public static JobOfferDto ToDto(this JobOffer job, DateTime now) => new()
    {
        Id = job.Id,
        Title = job.Title,
        Company = job.Company?.Name ?? string.Empty,
        CompanyId = job.CompanyId,
        Description = job.Description,
        SalaryMin = job.SalaryMin,
        SalaryMax = job.SalaryMax,
        Location = job.Location,
        WorkType = job.WorkType,
        CreatedAt = job.CreatedAt,
        Status = job.Status,
        ExpiresAt = job.ExpiresAt,
        IsExpired = job.IsExpired(now)
    };

    public static JobOffer ToEntity(this CreateJobOfferDto dto, int companyId)
    {
        var job = new JobOffer { CreatedAt = DateTime.UtcNow, Status = dto.Status, CompanyId = companyId };
        dto.ApplyFormTo(job);
        return job;
    }

    // Edycja oferty (bez Id i CreatedAt); brak statusu w żądaniu = status bez zmian,
    // niezmieniona data ważności zostaje zapisana z pełną precyzją z bazy.
    // companyId ustala kontroler (admin - z żądania, pracodawca - zawsze firma oferty)
    public static void ApplyTo(this UpdateJobOfferDto dto, JobOffer job, int companyId)
    {
        var storedExpiresAt = job.ExpiresAt;
        var keepsExpiresAt = dto.KeepsExpiresAtOf(job);

        dto.ApplyFormTo(job);
        job.Status = dto.Status ?? job.Status;

        if (job.CompanyId != companyId)
        {
            job.CompanyId = companyId;
            // nawigacja wskazywałaby poprzednią firmę
            job.Company = null!;
        }

        if (keepsExpiresAt)
        {
            job.ExpiresAt = storedExpiresAt;
        }
    }

    // Przepisuje pola wspólne dla tworzenia i edycji (bez firmy)
    private static void ApplyFormTo(this JobOfferFormDto dto, JobOffer job)
    {
        job.Title = dto.Title;
        job.Description = dto.Description;
        job.SalaryMin = dto.SalaryMin;
        job.SalaryMax = dto.SalaryMax;
        job.Location = dto.Location;
        job.WorkType = dto.WorkType;
        job.ExpiresAt = dto.GetExpiresAtUtc();
    }

    // Wymaga załadowanych nawigacji JobOffer (z Company) i User (Include) - inaczej pola pochodne będą puste.
    // includeHistory: dołącza historię (wymaga Include StatusHistory); includeChangedBy: autor zmian
    // (tylko dla admina i pracodawcy firmy oferty)
    public static JobApplicationDto ToDto(this JobApplication application, bool includeHistory = false, bool includeChangedBy = false) => new()
    {
        Id = application.Id,
        JobOfferId = application.JobOfferId,
        UserId = application.UserId,
        Note = application.Note,
        AppliedAt = application.AppliedAt,
        Status = application.Status,
        StatusChangedAt = application.StatusChangedAt,
        UserEmail = application.User?.Email ?? string.Empty,
        UserFullName = FullName(application.User),
        JobTitle = application.JobOffer?.Title ?? string.Empty,
        Company = application.JobOffer?.Company?.Name ?? string.Empty,
        CompanyId = application.JobOffer?.CompanyId ?? 0,
        JobOffer = application.JobOffer?.ToDto(),
        History = includeHistory
            ? (application.StatusHistory ?? [])
                .OrderBy(h => h.ChangedAt)
                .ThenBy(h => h.Id)
                .Select(h => h.ToDto(includeChangedBy))
                .ToList()
            : null
    };

    public static ApplicationStatusHistoryDto ToDto(this ApplicationStatusHistory entry, bool includeChangedBy = false) => new()
    {
        Status = entry.Status,
        ChangedAt = entry.ChangedAt,
        Comment = entry.Comment,
        ChangedBy = includeChangedBy ? DescribeUser(entry.ChangedByUser) : null
    };

    // includeReview: status, powód odrzucenia i data decyzji (admin i członkowie firmy);
    // membersCount tylko w odpowiedziach panelu admina
    public static CompanyDto ToDto(this Company company, int activeJobsCount, bool includeReview, int? membersCount = null) => new()
    {
        Id = company.Id,
        Name = company.Name,
        Description = company.Description,
        Website = company.Website,
        Location = company.Location,
        Status = includeReview ? company.Status : null,
        RejectionReason = includeReview ? company.RejectionReason : null,
        ReviewedAt = includeReview ? company.ReviewedAt : null,
        CreatedAt = company.CreatedAt,
        ActiveJobsCount = activeJobsCount,
        MembersCount = membersCount
    };

    public static CompanyDto ToDto(this CompanySummary summary, bool includeReview, bool includeMembers = false) =>
        summary.Company.ToDto(summary.ActiveJobsCount, includeReview, includeMembers ? summary.MembersCount : null);

    // Przepisuje dane z formularza (z normalizacją nazwy i pustych pól); status weryfikacji zmienia wywołujący
    public static void ApplyTo(this CompanyFormDto dto, Company company)
    {
        company.Name = CompanyNames.Normalize(dto.Name);
        company.Description = CompanyNames.NormalizeOptional(dto.Description);
        company.Website = CompanyNames.NormalizeOptional(dto.Website);
        company.Location = CompanyNames.NormalizeOptional(dto.Location);
    }

    private static string FullName(ApplicationUser? user) =>
        $"{user?.FirstName} {user?.LastName}".Trim();

    // "Imię Nazwisko (email)", sam email lub null, gdy konto autora zostało usunięte
    private static string? DescribeUser(ApplicationUser? user)
    {
        if (user == null) return null;

        var name = FullName(user);
        if (string.IsNullOrEmpty(name)) return user.Email;
        return string.IsNullOrEmpty(user.Email) ? name : $"{name} ({user.Email})";
    }
}
