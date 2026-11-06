namespace Backend.DTOs;

// Dokumenty z profilu kopiowane do aplikacji (POST /api/jobapplications/{id}/attachments/from-profile)
public class AttachFromProfileDto
{
    public List<int>? DocumentIds { get; set; }
}
