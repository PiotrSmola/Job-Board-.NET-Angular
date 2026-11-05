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
    public void UpdateJobOfferDto_ApplyTo_OverwritesEditableFieldsButNotIdOrCreatedAt()
    {
        var job = NewJob();
        var originalCreatedAt = job.CreatedAt;
        var dto = new UpdateJobOfferDto
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

    // --- cykl życia oferty i proces rekrutacji ---

    [Fact]
    public void JobOffer_ToDto_CopiesStatusAndExpiresAt()
    {
        var job = NewJob();
        job.Status = JobOfferStatus.Closed;
        job.ExpiresAt = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var dto = job.ToDto();

        Assert.Equal(JobOfferStatus.Closed, dto.Status);
        Assert.Equal(job.ExpiresAt, dto.ExpiresAt);
    }

    [Fact]
    public void JobOffer_ToDto_DefaultsToActiveAndOpenEnded()
    {
        var dto = NewJob().ToDto();

        Assert.Equal(JobOfferStatus.Active, dto.Status);
        Assert.Null(dto.ExpiresAt);
        Assert.False(dto.IsExpired);
    }

    [Theory]
    [InlineData(JobOfferStatus.Active, -1, true)]
    [InlineData(JobOfferStatus.Active, 0, true)]
    [InlineData(JobOfferStatus.Active, 1, false)]
    [InlineData(JobOfferStatus.Draft, -1, false)]
    [InlineData(JobOfferStatus.Closed, -1, false)]
    public void JobOffer_ToDto_ComputesIsExpiredAtTheGivenMoment(JobOfferStatus status, int expiresOffsetSeconds, bool expected)
    {
        var now = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var job = NewJob();
        job.Status = status;
        job.ExpiresAt = now.AddSeconds(expiresOffsetSeconds);

        Assert.Equal(expected, job.ToDto(now).IsExpired);
    }

    [Fact]
    public void JobOffer_ToDto_WithoutMoment_UsesCurrentTime()
    {
        var expired = NewJob();
        expired.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        var valid = NewJob();
        valid.ExpiresAt = DateTime.UtcNow.AddMinutes(10);

        Assert.True(expired.ToDto().IsExpired);
        Assert.False(valid.ToDto().IsExpired);
    }

    [Fact]
    public void CreateJobOfferDto_ToEntity_DefaultsToActiveWithoutExpiration()
    {
        var dto = new CreateJobOfferDto { Title = "T", Company = "C", Description = "D", Location = "L" };

        var job = dto.ToEntity();

        Assert.Equal(JobOfferStatus.Active, job.Status);
        Assert.Null(job.ExpiresAt);
    }

    [Fact]
    public void UpdateJobOfferDto_ApplyTo_CopiesStatusAndExpiresAt()
    {
        var job = NewJob();
        var expires = new DateTime(2031, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var dto = new UpdateJobOfferDto
        {
            Title = "T", Company = "C", Description = "D", Location = "L", Status = JobOfferStatus.Draft, ExpiresAt = expires
        };

        dto.ApplyTo(job);

        Assert.Equal(JobOfferStatus.Draft, job.Status);
        Assert.Equal(expires, job.ExpiresAt);
    }

    [Fact]
    public void UpdateJobOfferDto_ApplyTo_ClearsExpiresAtWhenMissing()
    {
        var job = NewJob();
        job.ExpiresAt = DateTime.UtcNow.AddDays(1);
        var dto = new UpdateJobOfferDto { Title = "T", Company = "C", Description = "D", Location = "L" };

        dto.ApplyTo(job);

        Assert.Null(job.ExpiresAt);
    }

    [Theory]
    [InlineData(JobOfferStatus.Draft)]
    [InlineData(JobOfferStatus.Active)]
    [InlineData(JobOfferStatus.Closed)]
    public void UpdateJobOfferDto_ApplyTo_WithoutStatusKeepsCurrentStatus(JobOfferStatus current)
    {
        var job = NewJob();
        job.Status = current;
        var dto = new UpdateJobOfferDto { Title = "Edited", Company = "C", Description = "D", Location = "L" };

        dto.ApplyTo(job);

        Assert.Equal(current, job.Status);
        Assert.Equal("Edited", job.Title);
    }

    [Fact]
    public void CreateJobOfferDto_ToEntity_UsesGivenStatus()
    {
        var dto = new CreateJobOfferDto { Title = "T", Company = "C", Description = "D", Location = "L", Status = JobOfferStatus.Closed };

        Assert.Equal(JobOfferStatus.Closed, dto.ToEntity().Status);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void CreateJobOfferDto_ExpiresAt_IsStoredAsUtcWithoutShiftingUnspecifiedTime(DateTimeKind kind)
    {
        var dto = new CreateJobOfferDto { ExpiresAt = new DateTime(2031, 3, 4, 10, 0, 0, kind) };

        var utc = dto.GetExpiresAtUtc();

        Assert.Equal(DateTimeKind.Utc, utc!.Value.Kind);
        Assert.Equal(new DateTime(2031, 3, 4, 10, 0, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void CreateJobOfferDto_ExpiresAt_LocalTimeIsConvertedToUtc()
    {
        var local = new DateTime(2031, 3, 4, 10, 0, 0, DateTimeKind.Local);
        var dto = new CreateJobOfferDto { ExpiresAt = local };

        var utc = dto.GetExpiresAtUtc();

        Assert.Equal(DateTimeKind.Utc, utc!.Value.Kind);
        Assert.Equal(local.ToUniversalTime(), utc);
    }

    [Fact]
    public void CreateJobOfferDto_GetExpiresAtUtc_IsNullWhenNotSet()
    {
        Assert.Null(new CreateJobOfferDto().GetExpiresAtUtc());
    }

    [Fact]
    public void JobApplication_ToDto_CopiesStatusAndStatusChangedAt()
    {
        var changedAt = new DateTime(2025, 7, 2, 9, 15, 0, DateTimeKind.Utc);
        var application = new JobApplication
        {
            Status = ApplicationStatus.Interview,
            StatusChangedAt = changedAt,
            JobOffer = NewJob(),
            User = new ApplicationUser()
        };

        var dto = application.ToDto();

        Assert.Equal(ApplicationStatus.Interview, dto.Status);
        Assert.Equal(changedAt, dto.StatusChangedAt);
    }

    [Fact]
    public void JobApplication_ToDto_OmitsHistoryByDefault()
    {
        var application = new JobApplication { JobOffer = NewJob(), User = new ApplicationUser() };
        application.ChangeStatus(ApplicationStatus.InReview, "recruiter", "ok", DateTime.UtcNow);

        Assert.Null(application.ToDto().History);
        Assert.Null(application.ToDto(includeChangedBy: true).History);
    }

    [Fact]
    public void JobApplication_ToDto_WithHistory_OrdersEntriesByChangedAtThenId()
    {
        var t0 = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var application = new JobApplication { JobOffer = NewJob(), User = new ApplicationUser() };
        // celowo w odwrotnej kolejności względem czasu; dwa wpisy z tym samym czasem rozstrzyga Id
        application.StatusHistory.Add(new ApplicationStatusHistory { Id = 3, Status = ApplicationStatus.Hired, ChangedAt = t0.AddHours(2) });
        application.StatusHistory.Add(new ApplicationStatusHistory { Id = 5, Status = ApplicationStatus.Interview, ChangedAt = t0.AddHours(1), Comment = "second" });
        application.StatusHistory.Add(new ApplicationStatusHistory { Id = 4, Status = ApplicationStatus.InReview, ChangedAt = t0.AddHours(1), Comment = "first" });
        application.StatusHistory.Add(new ApplicationStatusHistory { Id = 1, Status = ApplicationStatus.Submitted, ChangedAt = t0 });

        var history = application.ToDto(includeHistory: true).History!;

        Assert.Equal(
            new[] { ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.Interview, ApplicationStatus.Hired },
            history.Select(h => h.Status));
        Assert.Equal(new string?[] { null, "first", "second", null }, history.Select(h => h.Comment));
        Assert.Equal(t0, history[0].ChangedAt);
    }

    [Fact]
    public void JobApplication_ToDto_WithHistoryButNoEntries_ReturnsEmptyList()
    {
        var application = new JobApplication { JobOffer = NewJob(), User = new ApplicationUser() };

        var history = application.ToDto(includeHistory: true).History;

        Assert.NotNull(history);
        Assert.Empty(history!);
    }

    [Fact]
    public void JobApplication_ToDto_WithHistoryAndNullCollection_ReturnsEmptyList()
    {
        var application = new JobApplication { JobOffer = NewJob(), User = new ApplicationUser() };
        application.StatusHistory = null!;

        Assert.Empty(application.ToDto(includeHistory: true).History!);
    }

    [Fact]
    public void ApplicationStatusHistory_ToDto_HidesAuthorUnlessRequested()
    {
        var entry = new ApplicationStatusHistory
        {
            Status = ApplicationStatus.Offered,
            ChangedAt = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc),
            Comment = "Congratulations",
            ChangedByUser = new ApplicationUser { Email = "hr@example.com", FirstName = "Anna", LastName = "Nowak" }
        };

        var hidden = entry.ToDto();
        var shown = entry.ToDto(includeChangedBy: true);

        Assert.Equal(ApplicationStatus.Offered, hidden.Status);
        Assert.Equal(entry.ChangedAt, hidden.ChangedAt);
        Assert.Equal("Congratulations", hidden.Comment);
        Assert.Null(hidden.ChangedBy);
        Assert.Equal("Anna Nowak (hr@example.com)", shown.ChangedBy);
    }

    [Theory]
    [InlineData("Anna", "Nowak", "hr@example.com", "Anna Nowak (hr@example.com)")]
    [InlineData("Anna", null, "hr@example.com", "Anna (hr@example.com)")]
    [InlineData(null, null, "hr@example.com", "hr@example.com")]
    [InlineData("Anna", "Nowak", null, "Anna Nowak")]
    public void ApplicationStatusHistory_ToDto_DescribesAuthorByNameAndEmail(string? first, string? last, string? email, string expected)
    {
        var entry = new ApplicationStatusHistory
        {
            ChangedByUser = new ApplicationUser { FirstName = first, LastName = last, Email = email }
        };

        Assert.Equal(expected, entry.ToDto(includeChangedBy: true).ChangedBy);
    }

    [Fact]
    public void ApplicationStatusHistory_ToDto_ForDeletedAuthor_HasNullChangedBy()
    {
        var entry = new ApplicationStatusHistory { ChangedByUserId = null, ChangedByUser = null };

        Assert.Null(entry.ToDto(includeChangedBy: true).ChangedBy);
    }

    [Fact]
    public void JobApplication_ToDto_PassesChangedByFlagToHistoryEntries()
    {
        var application = new JobApplication { JobOffer = NewJob(), User = new ApplicationUser() };
        application.StatusHistory.Add(new ApplicationStatusHistory
        {
            Status = ApplicationStatus.Submitted,
            ChangedByUser = new ApplicationUser { Email = "jan@example.com" }
        });

        Assert.Null(application.ToDto(includeHistory: true).History![0].ChangedBy);
        Assert.Equal("jan@example.com", application.ToDto(includeHistory: true, includeChangedBy: true).History![0].ChangedBy);
    }
}
