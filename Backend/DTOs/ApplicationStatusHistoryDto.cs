using Backend.Models;

namespace Backend.DTOs;

public class ApplicationStatusHistoryDto
{
    public ApplicationStatus Status { get; set; }
    public DateTime ChangedAt { get; set; }

    // komentarz rekrutera jest widoczny także dla kandydata
    public string? Comment { get; set; }

    // autor zmiany - wypełniany tylko w odpowiedziach dla admina
    public string? ChangedBy { get; set; }
}
