using Backend.DTOs;
using Backend.Models;

namespace Backend.Mappings;

// Ręczne mapowanie encji na DTO (jedno miejsce zamiast kopii w kontrolerach)
public static class MappingExtensions
{
    public static JobOfferDto ToDto(this JobOffer job) => job.ToDto(DateTime.UtcNow);

    public static JobOfferDto ToDto(this JobOffer job, DateTime now) => new()
    {
        Id = job.Id,
        Title = job.Title,
        Company = job.Company,
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

    public static JobOffer ToEntity(this CreateJobOfferDto dto)
    {
        var job = new JobOffer { CreatedAt = DateTime.UtcNow };
        dto.ApplyTo(job);
        return job;
    }

    // Przepisuje edytowalne pola oferty (bez Id i CreatedAt)
    public static void ApplyTo(this CreateJobOfferDto dto, JobOffer job)
    {
        job.Title = dto.Title;
        job.Company = dto.Company;
        job.Description = dto.Description;
        job.SalaryMin = dto.SalaryMin;
        job.SalaryMax = dto.SalaryMax;
        job.Location = dto.Location;
        job.WorkType = dto.WorkType;
        job.Status = dto.Status;
        job.ExpiresAt = dto.GetExpiresAtUtc();
    }

    // Wymaga załadowanych nawigacji JobOffer i User (Include) - inaczej pola pochodne będą puste.
    // includeHistory: dołącza historię (wymaga Include StatusHistory); includeChangedBy: autor zmian (tylko dla admina)
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
        Company = application.JobOffer?.Company ?? string.Empty,
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
