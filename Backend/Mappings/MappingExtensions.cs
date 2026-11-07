using Backend.DTOs;
using Backend.Helpers;
using Backend.Models;

namespace Backend.Mappings;

// Ręczne mapowanie encji na DTO (jedno miejsce zamiast kopii w kontrolerach)
public static class MappingExtensions
{
    public static JobOfferDto ToDto(this JobOffer job) => job.ToDto(DateTime.UtcNow);

    // Wymaga załadowanych nawigacji Company, Category i Skills (Include) - inaczej nazwa firmy będzie pusta,
    // a kategoria i umiejętności brakujące
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
        IsExpired = job.IsExpired(now),
        CategoryId = job.CategoryId,
        CategoryName = job.Category?.Name,
        CategorySlug = job.Category?.Slug,
        Seniority = job.Seniority,
        ContractTypes = job.ContractTypes,
        SalaryCurrency = job.SalaryCurrency,
        SalaryPeriod = job.SalaryPeriod,
        Skills = (job.Skills ?? [])
            .Select(s => s.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n, StringComparer.Ordinal)
            .ToList(),
        // tło z encji firmy wczytanej razem z ofertą (Include) - bez dodatkowych zapytań; publiczne tylko dla firmy
        // zatwierdzonej (obraz firmy niezatwierdzonej nie jest dostępny anonimowo)
        CompanyBackground = job.Company?.Status == CompanyStatus.Approved ? job.Company.ToOfferBackgroundDto() : null
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

    // Przepisuje pola wspólne dla tworzenia i edycji (bez firmy i umiejętności - te zapisuje repozytorium)
    private static void ApplyFormTo(this JobOfferFormDto dto, JobOffer job)
    {
        job.Title = dto.Title;
        job.Description = dto.Description;
        job.SalaryMin = dto.SalaryMin;
        job.SalaryMax = dto.SalaryMax;
        job.Location = dto.Location;
        job.WorkType = dto.WorkType;
        job.ExpiresAt = dto.GetExpiresAtUtc();
        job.CategoryId = dto.CategoryId;
        job.Seniority = dto.Seniority;
        job.ContractTypes = dto.ContractTypes;
        job.SalaryCurrency = dto.SalaryCurrency;
        job.SalaryPeriod = dto.SalaryPeriod;
    }

    // Wymaga załadowanych nawigacji JobOffer (z Company), User i Attachments (Include) - inaczej pola pochodne będą puste.
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
        Attachments = (application.Attachments ?? [])
            .OrderBy(a => a.Kind)
            .ThenBy(a => a.UploadedAt)
            .ThenBy(a => a.Id)
            .Select(a => a.ToDto())
            .ToList(),
        AttachmentsTotalSize = (application.Attachments ?? []).Sum(a => a.Size),
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

    // Bez klucza pliku i content type - dane magazynu nie trafiają do API
    public static ApplicationAttachmentDto ToDto(this ApplicationAttachment attachment) => new()
    {
        Id = attachment.Id,
        Kind = attachment.Kind,
        FileName = attachment.FileName,
        Size = attachment.Size,
        UploadedAt = attachment.UploadedAt
    };

    // Dokument profilu - bez klucza pliku i content type
    public static ProfileDocumentDto ToDto(this ProfileDocument document) => new()
    {
        Id = document.Id,
        Kind = document.Kind,
        FileName = document.FileName,
        Size = document.Size,
        UploadedAt = document.UploadedAt
    };

    // Dokumenty profilu w kolejności jak załączniki aplikacji (rodzaj, data dodania)
    public static List<ProfileDocumentDto> ToDtos(this IEnumerable<ProfileDocument> documents) => documents
        .OrderBy(d => d.Kind)
        .ThenBy(d => d.UploadedAt)
        .ThenBy(d => d.Id)
        .Select(d => d.ToDto())
        .ToList();

    public static ProfileDto ToProfileDto(this ApplicationUser user, IEnumerable<string> roles) => new()
    {
        Email = user.Email ?? string.Empty,
        FirstName = user.FirstName ?? string.Empty,
        LastName = user.LastName ?? string.Empty,
        PhoneNumber = user.PhoneNumber,
        Headline = user.Headline,
        Location = user.Location,
        LinkedInUrl = user.LinkedInUrl,
        About = user.About,
        EmailConfirmed = user.EmailConfirmed,
        Roles = roles.Order(StringComparer.Ordinal).ToList(),
        CreatedAt = user.CreatedAt
    };

    public static NotificationPreferencesDto ToNotificationPreferencesDto(this ApplicationUser user) => new()
    {
        ApplicationStatusChanged = user.NotifyApplicationStatusChanged,
        NewApplication = user.NotifyNewApplication,
        CompanyReview = user.NotifyCompanyReview
    };

    // includeReview: status, powód odrzucenia i data decyzji (admin i członkowie firmy);
    // membersCount tylko w odpowiedziach panelu admina
    public static CategoryDto ToDto(this CategorySummary summary) => new()
    {
        Id = summary.Category.Id,
        Name = summary.Category.Name,
        Slug = summary.Category.Slug,
        SortOrder = summary.Category.SortOrder,
        ActiveJobsCount = summary.ActiveJobsCount
    };

    public static JobFacetsDto ToDto(this JobFacets facets) => new()
    {
        Total = facets.Total,
        Categories = facets.Categories
            .Select(c => new FacetCategoryDto { Id = c.Id, Slug = c.Slug, Name = c.Name, Count = c.Count })
            .ToList(),
        Seniority = facets.Seniority.Select(ToFacetValueDto).ToList(),
        ContractTypes = facets.ContractTypes.Select(ToFacetValueDto).ToList(),
        WorkTypes = facets.WorkTypes.Select(ToFacetValueDto).ToList(),
        Skills = facets.Skills.Select(s => new FacetSkillDto { Name = s.Name, Count = s.JobsCount }).ToList()
    };

    private static FacetValueDto ToFacetValueDto(FacetCount count) => new() { Value = count.Value, Count = count.Count };

    public static CompanyDto ToDto(this Company company, int activeJobsCount, bool includeReview, int? membersCount = null,
        int photosCount = 0) => new()
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
        MembersCount = membersCount,
        PhotosCount = photosCount,
        OfferBackground = company.ToOfferBackgroundDto()
    };

    public static CompanyDto ToDto(this CompanySummary summary, bool includeReview, bool includeMembers = false) =>
        summary.Company.ToDto(summary.ActiveJobsCount, includeReview, includeMembers ? summary.MembersCount : null,
            summary.PhotosCount);

    // Tło ofert firmy; null - firma bez obrazu tła (chyba że allowWithoutImage - odpowiedź zmiany ustawień)
    public static OfferBackgroundDto? ToOfferBackgroundDto(this Company company, bool allowWithoutImage = false) =>
        company.OfferBackgroundKey == null && !allowWithoutImage
            ? null
            : new OfferBackgroundDto
            {
                Url = company.OfferBackgroundKey == null ? null : CompanyMedia.BackgroundUrl(company.Id, company.OfferBackgroundKey),
                Dim = company.OfferBackgroundDim,
                Focus = company.OfferBackgroundFocus
            };

    // Zdjęcie galerii - bez klucza pliku, typu i rozmiaru
    public static CompanyPhotoDto ToDto(this CompanyPhoto photo) => new()
    {
        Id = photo.Id,
        Caption = photo.Caption,
        Width = photo.Width,
        Height = photo.Height,
        Url = CompanyMedia.PhotoUrl(photo.CompanyId, photo.Id)
    };

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
