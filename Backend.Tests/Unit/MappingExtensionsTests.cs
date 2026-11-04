using Backend.DTOs;
using Backend.Mappings;
using Backend.Models;

namespace Backend.Tests.Unit;

public class MappingExtensionsTests
{
    private static JobOffer NewJob() => new()
    {
        Id = 42,
        Title = "Title",
        Company = "Company",
        Description = "Description",
        SalaryMin = 1000,
        SalaryMax = 2000,
        Location = "Location",
        WorkType = WorkType.Hybrid,
        CreatedAt = new DateTime(2025, 5, 17, 10, 30, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void JobOffer_ToDto_CopiesAllFields()
    {
        var job = NewJob();

        var dto = job.ToDto();

        Assert.Equal(42, dto.Id);
        Assert.Equal("Title", dto.Title);
        Assert.Equal("Company", dto.Company);
        Assert.Equal("Description", dto.Description);
        Assert.Equal(1000, dto.SalaryMin);
        Assert.Equal(2000, dto.SalaryMax);
        Assert.Equal("Location", dto.Location);
        Assert.Equal(WorkType.Hybrid, dto.WorkType);
        Assert.Equal(job.CreatedAt, dto.CreatedAt);
    }

    [Fact]
    public void JobOffer_ToDto_KeepsMissingSalaryAsNull()
    {
        var job = NewJob();
        job.SalaryMin = null;
        job.SalaryMax = null;

        var dto = job.ToDto();

        Assert.Null(dto.SalaryMin);
        Assert.Null(dto.SalaryMax);
    }

    [Fact]
    public void CreateJobOfferDto_ToEntity_CopiesFieldsAndSetsCreatedAtToNow()
    {
        var dto = new CreateJobOfferDto
        {
            Title = "T", Company = "C", Description = "D", SalaryMin = 1, SalaryMax = 2, Location = "L", WorkType = WorkType.Onsite
        };
        var before = DateTime.UtcNow;

        var job = dto.ToEntity();

        Assert.Equal(0, job.Id);
        Assert.Equal("T", job.Title);
        Assert.Equal("C", job.Company);
        Assert.Equal("D", job.Description);
        Assert.Equal(1, job.SalaryMin);
        Assert.Equal(2, job.SalaryMax);
        Assert.Equal("L", job.Location);
        Assert.Equal(WorkType.Onsite, job.WorkType);
        Assert.InRange(job.CreatedAt, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, job.CreatedAt.Kind);
    }

    [Fact]
    public void CreateJobOfferDto_ApplyTo_OverwritesEditableFieldsButNotIdOrCreatedAt()
    {
        var job = NewJob();
        var originalCreatedAt = job.CreatedAt;
        var dto = new CreateJobOfferDto
        {
            Title = "New", Company = "New C", Description = "New D", SalaryMin = null, SalaryMax = 9, Location = "New L", WorkType = WorkType.Remote
        };

        dto.ApplyTo(job);

        Assert.Equal(42, job.Id);
        Assert.Equal(originalCreatedAt, job.CreatedAt);
        Assert.Equal("New", job.Title);
        Assert.Equal("New C", job.Company);
        Assert.Equal("New D", job.Description);
        Assert.Null(job.SalaryMin);
        Assert.Equal(9, job.SalaryMax);
        Assert.Equal("New L", job.Location);
        Assert.Equal(WorkType.Remote, job.WorkType);
    }

    [Fact]
    public void JobApplication_ToDto_WithLoadedNavigations_FillsDerivedFields()
    {
        var job = NewJob();
        var application = new JobApplication
        {
            Id = 7,
            JobOfferId = 42,
            UserId = "user-1",
            Note = "Hello",
            AppliedAt = new DateTime(2025, 6, 1, 8, 0, 0, DateTimeKind.Utc),
            JobOffer = job,
            User = new ApplicationUser { Email = "jan@example.com", FirstName = "Jan", LastName = "Kowalski" }
        };

        var dto = application.ToDto();

        Assert.Equal(7, dto.Id);
        Assert.Equal(42, dto.JobOfferId);
        Assert.Equal("user-1", dto.UserId);
        Assert.Equal("Hello", dto.Note);
        Assert.Equal(application.AppliedAt, dto.AppliedAt);
        Assert.Equal("jan@example.com", dto.UserEmail);
        Assert.Equal("Jan Kowalski", dto.UserFullName);
        Assert.Equal("Title", dto.JobTitle);
        Assert.Equal("Company", dto.Company);
        Assert.NotNull(dto.JobOffer);
        Assert.Equal(42, dto.JobOffer!.Id);
        Assert.Equal("Title", dto.JobOffer.Title);
    }

    [Fact]
    public void JobApplication_ToDto_WithoutNavigations_DoesNotThrowAndLeavesDerivedFieldsEmpty()
    {
        var application = new JobApplication { Id = 1, JobOfferId = 5, UserId = "u" };
        // nawigacje mają domyślnie null! - symulujemy brak Include
        application.JobOffer = null!;
        application.User = null!;

        var dto = application.ToDto();

        Assert.Equal(string.Empty, dto.UserEmail);
        Assert.Equal(string.Empty, dto.UserFullName);
        Assert.Equal(string.Empty, dto.JobTitle);
        Assert.Equal(string.Empty, dto.Company);
        Assert.Null(dto.JobOffer);
    }

    [Theory]
    [InlineData("Jan", null, "Jan")]
    [InlineData(null, "Kowalski", "Kowalski")]
    [InlineData(null, null, "")]
    [InlineData("Jan", "Kowalski", "Jan Kowalski")]
    public void JobApplication_ToDto_TrimsFullNameWhenPartsAreMissing(string? first, string? last, string expected)
    {
        var application = new JobApplication
        {
            User = new ApplicationUser { FirstName = first, LastName = last },
            JobOffer = NewJob()
        };

        Assert.Equal(expected, application.ToDto().UserFullName);
    }
}
