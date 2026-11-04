using Backend.DTOs;
using Backend.Models;

namespace Backend.Mappings;

// Ręczne mapowanie encji na DTO (jedno miejsce zamiast kopii w kontrolerach)
public static class MappingExtensions
{
    public static JobOfferDto ToDto(this JobOffer job) => new()
    {
        Id = job.Id,
        Title = job.Title,
        Company = job.Company,
        Description = job.Description,
        SalaryMin = job.SalaryMin,
        SalaryMax = job.SalaryMax,
        Location = job.Location,
        WorkType = job.WorkType,
        CreatedAt = job.CreatedAt
    };

    public static JobOffer ToEntity(this CreateJobOfferDto dto)
    {
        var job = new JobOffer { CreatedAt = DateTime.UtcNow };
        dto.ApplyTo(job);
        return job;
    }

    // Przepisuje edytowalne pola oferty (bez Id i CreatedAt)
    public static void ApplyTo(this CreateJobOfferDto dto, JobOffer job)
    {
        job.Title = dto.Title;
        job.Company = dto.Company;
        job.Description = dto.Description;
        job.SalaryMin = dto.SalaryMin;
        job.SalaryMax = dto.SalaryMax;
        job.Location = dto.Location;
        job.WorkType = dto.WorkType;
    }

    // Wymaga załadowanych nawigacji JobOffer i User (Include) - inaczej pola pochodne będą puste
    public static JobApplicationDto ToDto(this JobApplication application) => new()
    {
        Id = application.Id,
        JobOfferId = application.JobOfferId,
        UserId = application.UserId,
        Note = application.Note,
        AppliedAt = application.AppliedAt,
        UserEmail = application.User?.Email ?? string.Empty,
        UserFullName = $"{application.User?.FirstName} {application.User?.LastName}".Trim(),
        JobTitle = application.JobOffer?.Title ?? string.Empty,
        Company = application.JobOffer?.Company ?? string.Empty,
        JobOffer = application.JobOffer?.ToDto()
    };
}
