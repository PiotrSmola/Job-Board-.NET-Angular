using Backend.Models;

namespace Backend.DTOs;

public class CompanyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Website { get; set; }
    public string? Location { get; set; }

    // status weryfikacji, powód odrzucenia i data decyzji - tylko dla admina i członków firmy (publicznie null)
    public CompanyStatus? Status { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    // liczba ofert widocznych publicznie (aktywnych i niewygasłych); 0 dla firmy niezatwierdzonej
    public int ActiveJobsCount { get; set; }

    // liczba kont pracodawców firmy - tylko w odpowiedziach /api/admin/companies
    public int? MembersCount { get; set; }
}
