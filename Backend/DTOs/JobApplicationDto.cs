using System.ComponentModel.DataAnnotations;
using Backend.Models;

namespace Backend.DTOs;

public class JobApplicationDto
{
    public int Id { get; set; }
    public int JobOfferId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime AppliedAt { get; set; }

    // Related data
    public string UserEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public JobOffer? JobOffer { get; set; }
}
