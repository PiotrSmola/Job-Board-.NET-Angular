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
    public string Company { get; set; } = string.Empty;
    public JobOfferDto? JobOffer { get; set; }

    // historia chronologicznie - tylko w GET /api/jobapplications/{id} i odpowiedziach na zmianę statusu (w listach null)
    public List<ApplicationStatusHistoryDto>? History { get; set; }
}
