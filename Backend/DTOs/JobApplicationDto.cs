using Backend.Models;

namespace Backend.DTOs;

public class JobApplicationDto
{
    public int Id { get; set; }
    public int JobOfferId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime AppliedAt { get; set; }
    public ApplicationStatus Status { get; set; }
    public DateTime StatusChangedAt { get; set; }

    public string UserEmail { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    // nazwa firmy oferty
    public string Company { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public JobOfferDto? JobOffer { get; set; }

    // załączniki posortowane wg rodzaju i daty dodania (bez klucza pliku w magazynie) i ich łączny rozmiar w bajtach
    public List<ApplicationAttachmentDto> Attachments { get; set; } = [];
    public long AttachmentsTotalSize { get; set; }

    // historia chronologicznie - tylko w GET /api/jobapplications/{id} i odpowiedziach na zmianę statusu (w listach null)
    public List<ApplicationStatusHistoryDto>? History { get; set; }
}
