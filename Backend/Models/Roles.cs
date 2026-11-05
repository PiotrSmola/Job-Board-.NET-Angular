namespace Backend.Models;

// Nazwy ról Identity (claim role w JWT)
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
    public const string Employer = "Employer";

    // dla [Authorize(Roles = ...)] - lista ról rozdzielona przecinkami
    public const string AdminOrEmployer = Admin + "," + Employer;

    public static readonly string[] All = { Admin, User, Employer };
}
