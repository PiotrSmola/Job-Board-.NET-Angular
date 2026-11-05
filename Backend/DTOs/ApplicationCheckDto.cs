using Backend.Models;

namespace Backend.DTOs;

// Odpowiedź GET /api/jobapplications/check/{jobId}
public class ApplicationCheckDto
{
    // true, gdy istnieje aplikacja w statusie innym niż Withdrawn
    public bool HasApplied { get; set; }

    // wypełnione, gdy użytkownik ma jakąkolwiek aplikację na ofertę (także wycofaną)
    public int? ApplicationId { get; set; }
    public ApplicationStatus? Status { get; set; }
}
