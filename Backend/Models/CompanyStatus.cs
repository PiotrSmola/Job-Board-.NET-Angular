namespace Backend.Models;

// Status weryfikacji firmy przez admina; oferty są publiczne tylko dla firm zatwierdzonych
public enum CompanyStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}
