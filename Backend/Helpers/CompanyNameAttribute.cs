using System.ComponentModel.DataAnnotations;

namespace Backend.Helpers;

// Nazwa firmy musi zawierać widoczne znaki także po normalizacji (sam zero-width space przechodzi przez [Required]);
// puste wartości przepuszcza (od tego jest [Required])
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class CompanyNameAttribute : ValidationAttribute
{
    public CompanyNameAttribute() : base("The {0} field must contain visible characters.")
    {
    }

    public override bool IsValid(object? value) =>
        value is not string text || string.IsNullOrWhiteSpace(text) || CompanyNames.Normalize(text).Length > 0;
}
