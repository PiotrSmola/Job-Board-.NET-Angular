using System.ComponentModel.DataAnnotations;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Models;

namespace Backend.Tests.Unit;

// Reguły domenowe cyklu życia oferty i procesu rekrutacji oraz walidacja DTO (bez bazy i HTTP)
public class RecruitmentModelTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static CreateJobOfferDto ValidJob(Action<CreateJobOfferDto>? customize = null)
    {
        var dto = new CreateJobOfferDto { Title = "T", Company = "C", Description = "D", Location = "L" };
        customize?.Invoke(dto);
        return dto;
    }

    // --- JobOffer ---

    [Theory]
    [InlineData(JobOfferStatus.Active, null, false, true)]
    [InlineData(JobOfferStatus.Active, 3600, false, true)]
    [InlineData(JobOfferStatus.Active, 1, false, true)]
    [InlineData(JobOfferStatus.Active, 0, true, false)]
    [InlineData(JobOfferStatus.Active, -1, true, false)]
    [InlineData(JobOfferStatus.Draft, null, false, false)]
    [InlineData(JobOfferStatus.Draft, -3600, false, false)]
    [InlineData(JobOfferStatus.Closed, null, false, false)]
    [InlineData(JobOfferStatus.Closed, 3600, false, false)]
    [InlineData(JobOfferStatus.Closed, -3600, false, false)]
    public void JobOffer_ExpirationAndVisibility_DependOnStatusAndExpiresAt(JobOfferStatus status, int? expiresOffsetSeconds, bool expired, bool visible)
    {
        var job = new JobOffer
        {
            Status = status,
            ExpiresAt = expiresOffsetSeconds.HasValue ? Now.AddSeconds(expiresOffsetSeconds.Value) : null
        };

        Assert.Equal(expired, job.IsExpired(Now));
        Assert.Equal(visible, job.IsPubliclyVisible(Now));
    }

    [Fact]
    public void JobOffer_NewInstance_IsActiveAndOpenEnded()
    {
        var job = new JobOffer();

        Assert.Equal(JobOfferStatus.Active, job.Status);
        Assert.Null(job.ExpiresAt);
    }

    // --- JobApplication ---

    [Fact]
    public void JobApplication_Submit_CreatesSubmittedApplicationWithInitialHistoryEntry()
    {
        var application = JobApplication.Submit(5, "user-1", "My note", Now);

        Assert.Equal(5, application.JobOfferId);
        Assert.Equal("user-1", application.UserId);
        Assert.Equal("My note", application.Note);
        Assert.Equal(ApplicationStatus.Submitted, application.Status);
        Assert.Equal(Now, application.AppliedAt);
        Assert.Equal(Now, application.StatusChangedAt);
        var entry = Assert.Single(application.StatusHistory);
        Assert.Equal(ApplicationStatus.Submitted, entry.Status);
        Assert.Equal(Now, entry.ChangedAt);
        Assert.Equal("user-1", entry.ChangedByUserId);
        Assert.Null(entry.Comment);
    }

    [Fact]
    public void JobApplication_ChangeStatus_UpdatesStatusAndTimestampAndAppendsEntry()
    {
        var application = JobApplication.Submit(1, "candidate", null, Now);
        var later = Now.AddHours(3);

        var entry = application.ChangeStatus(ApplicationStatus.Interview, "recruiter", "Call tomorrow", later);

        Assert.Equal(ApplicationStatus.Interview, application.Status);
        Assert.Equal(later, application.StatusChangedAt);
        Assert.Equal(Now, application.AppliedAt);
        Assert.Equal(2, application.StatusHistory.Count);
        Assert.Same(entry, application.StatusHistory.Last());
        Assert.Equal(ApplicationStatus.Interview, entry.Status);
        Assert.Equal(later, entry.ChangedAt);
        Assert.Equal("recruiter", entry.ChangedByUserId);
        Assert.Equal("Call tomorrow", entry.Comment);
    }

    [Fact]
    public void JobApplication_ChangeStatus_ToSameStatus_AddsNoteWithoutTouchingStatusChangedAt()
    {
        var application = JobApplication.Submit(1, "candidate", null, Now);

        var entry = application.ChangeStatus(ApplicationStatus.Submitted, "recruiter", "A note", Now.AddDays(1));

        Assert.Equal(ApplicationStatus.Submitted, application.Status);
        Assert.Equal(Now, application.StatusChangedAt);
        Assert.Equal(2, application.StatusHistory.Count);
        Assert.Equal("A note", entry.Comment);
        Assert.Equal(Now.AddDays(1), entry.ChangedAt);
    }

    [Fact]
    public void JobApplication_ChangeStatus_AllowsNullAuthor()
    {
        var application = new JobApplication();

        var entry = application.ChangeStatus(ApplicationStatus.Rejected, null, null, Now);

        Assert.Null(entry.ChangedByUserId);
    }

    [Theory]
    [InlineData(ApplicationStatus.Submitted, true)]
    [InlineData(ApplicationStatus.InReview, true)]
    [InlineData(ApplicationStatus.Interview, true)]
    [InlineData(ApplicationStatus.Offered, true)]
    [InlineData(ApplicationStatus.Hired, false)]
    [InlineData(ApplicationStatus.Rejected, false)]
    [InlineData(ApplicationStatus.Withdrawn, false)]
    public void JobApplication_CanBeWithdrawn_OnlyBeforeFinalDecision(ApplicationStatus status, bool expected)
    {
        var application = new JobApplication { Status = status };

        Assert.Equal(expected, application.CanBeWithdrawn);
    }

    [Fact]
    public void JobApplication_NewInstance_StartsAsSubmitted()
    {
        Assert.Equal(ApplicationStatus.Submitted, new JobApplication().Status);
    }

    [Fact]
    public void Enums_KeepTheNumericValuesUsedByTheApiAndDatabase()
    {
        Assert.Equal(
            new[] { 0, 1, 2, 3, 4, 5, 6 },
            new[]
            {
                ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.Interview, ApplicationStatus.Offered,
                ApplicationStatus.Hired, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn
            }.Select(s => (int)s));
        Assert.Equal(new[] { 0, 1, 2 }, new[] { JobOfferStatus.Draft, JobOfferStatus.Active, JobOfferStatus.Closed }.Select(s => (int)s));
    }

    // --- CreateJobOfferDto ---

    [Fact]
    public void CreateJobOfferDto_ActiveWithPastExpiresAt_FailsOnExpiresAt()
    {
        var dto = ValidJob(j => { j.Status = JobOfferStatus.Active; j.ExpiresAt = DateTime.UtcNow.AddMinutes(-1); });

        var error = Assert.Single(Validate(dto));

        Assert.Contains(nameof(CreateJobOfferDto.ExpiresAt), error.MemberNames);
        Assert.Equal("ExpiresAt must be in the future for an active job offer", error.ErrorMessage);
    }

    [Fact]
    public void CreateJobOfferDto_ActiveWithoutExplicitStatusAndPastExpiresAt_Fails()
    {
        // domyślny status to Active
        var dto = ValidJob(j => j.ExpiresAt = DateTime.UtcNow.AddDays(-1));

        Assert.Single(Validate(dto));
    }

    [Theory]
    [InlineData(JobOfferStatus.Draft)]
    [InlineData(JobOfferStatus.Closed)]
    public void CreateJobOfferDto_DraftOrClosedWithPastExpiresAt_IsValid(JobOfferStatus status)
    {
        var dto = ValidJob(j => { j.Status = status; j.ExpiresAt = DateTime.UtcNow.AddDays(-30); });

        Assert.Empty(Validate(dto));
    }

    [Fact]
    public void CreateJobOfferDto_ActiveWithFutureOrMissingExpiresAt_IsValid()
    {
        Assert.Empty(Validate(ValidJob(j => j.ExpiresAt = DateTime.UtcNow.AddDays(1))));
        Assert.Empty(Validate(ValidJob()));
    }

    [Fact]
    public void CreateJobOfferDto_UnspecifiedKindPastDate_IsTreatedAsUtcWhenValidating()
    {
        var past = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-1), DateTimeKind.Unspecified);
        var future = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(1), DateTimeKind.Unspecified);

        Assert.Single(Validate(ValidJob(j => j.ExpiresAt = past)));
        Assert.Empty(Validate(ValidJob(j => j.ExpiresAt = future)));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(100)]
    public void CreateJobOfferDto_UndefinedStatus_IsInvalid(int status)
    {
        var dto = ValidJob(j => j.Status = (JobOfferStatus)status);

        var error = Assert.Single(Validate(dto));

        Assert.Contains(nameof(CreateJobOfferDto.Status), error.MemberNames);
    }

    [Fact]
    public void CreateJobOfferDto_SalaryRangeAndExpirationErrorsAreReportedTogether()
    {
        var dto = ValidJob(j => { j.SalaryMin = 10; j.SalaryMax = 5; j.ExpiresAt = DateTime.UtcNow.AddDays(-1); });

        var errors = Validate(dto);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateJobOfferDto.ExpiresAt)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateJobOfferDto.SalaryMin)));
    }

    // --- UpdateJobOfferDto ---

    private static JobOffer StoredJob(JobOfferStatus status, DateTime? expiresAt) =>
        new() { Id = 1, Title = "T", Company = "C", Description = "D", Location = "L", Status = status, ExpiresAt = expiresAt };

    private static UpdateJobOfferDto UpdateJob(JobOfferStatus? status, DateTime? expiresAt) =>
        new() { Title = "T", Company = "C", Description = "D", Location = "L", Status = status, ExpiresAt = expiresAt };

    [Fact]
    public void UpdateJobOfferDto_WithoutStatus_IsValidAndHasNoStatus()
    {
        var dto = UpdateJob(null, Now.AddDays(-1));

        // reguła daty zależy od zapisanej oferty, więc sama walidacja DTO jej nie sprawdza
        Assert.Empty(Validate(dto));
        Assert.Null(dto.Status);
    }

    [Theory]
    [InlineData(JobOfferStatus.Draft)]
    [InlineData(JobOfferStatus.Closed)]
    public void UpdateJobOfferDto_WithoutStatusAndPastDate_UsesStoredNonActiveStatus(JobOfferStatus stored)
    {
        Assert.Null(UpdateJob(null, Now.AddDays(-1)).ValidateExpiresAtAgainst(StoredJob(stored, null), Now));
    }

    [Fact]
    public void UpdateJobOfferDto_WithoutStatusAndPastDate_FailsForStoredActiveOffer()
    {
        var error = UpdateJob(null, Now.AddDays(-1)).ValidateExpiresAtAgainst(StoredJob(JobOfferStatus.Active, Now.AddDays(3)), Now);

        Assert.NotNull(error);
        Assert.Contains(nameof(UpdateJobOfferDto.ExpiresAt), error!.MemberNames);
    }

    [Theory]
    [InlineData(JobOfferStatus.Draft)]
    [InlineData(JobOfferStatus.Closed)]
    public void UpdateJobOfferDto_PublishingWithPastDate_Fails(JobOfferStatus stored)
    {
        Assert.NotNull(UpdateJob(JobOfferStatus.Active, Now.AddDays(-1)).ValidateExpiresAtAgainst(StoredJob(stored, Now.AddDays(-1)), Now));
    }

    [Fact]
    public void UpdateJobOfferDto_UndefinedStatus_IsInvalid()
    {
        var error = Assert.Single(Validate(UpdateJob((JobOfferStatus)7, null)));

        Assert.Contains(nameof(UpdateJobOfferDto.Status), error.MemberNames);
    }

    // --- DTO zmiany statusu ---

    [Fact]
    public void UpdateApplicationStatusDto_WithoutStatus_IsInvalid()
    {
        var errors = Validate(new UpdateApplicationStatusDto());

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(UpdateApplicationStatusDto.Status)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void UpdateApplicationStatusDto_WithDefinedStatus_IsValid(int status)
    {
        Assert.Empty(Validate(new UpdateApplicationStatusDto { Status = (ApplicationStatus)status }));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(-1)]
    public void UpdateApplicationStatusDto_WithUndefinedStatus_IsInvalid(int status)
    {
        Assert.NotEmpty(Validate(new UpdateApplicationStatusDto { Status = (ApplicationStatus)status }));
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public void UpdateApplicationStatusDto_CommentLimitIs1000Characters(int length, bool valid)
    {
        var dto = new UpdateApplicationStatusDto { Status = ApplicationStatus.InReview, Comment = new string('x', length) };

        Assert.Equal(valid, Validate(dto).Count == 0);
    }

    [Fact]
    public void UpdateJobOfferStatusDto_RequiresDefinedStatus()
    {
        Assert.NotEmpty(Validate(new UpdateJobOfferStatusDto()));
        Assert.NotEmpty(Validate(new UpdateJobOfferStatusDto { Status = (JobOfferStatus)9 }));
        Assert.Empty(Validate(new UpdateJobOfferStatusDto { Status = JobOfferStatus.Draft }));
    }

    // --- parametry zapytań ---

    [Theory]
    [InlineData(null, true)]
    [InlineData("draft", true)]
    [InlineData("active", true)]
    [InlineData("expired", true)]
    [InlineData("closed", true)]
    [InlineData("Draft", true)]
    [InlineData("CLOSED", true)]
    [InlineData("all", false)]
    [InlineData("", true)]
    [InlineData("draft ", false)]
    [InlineData("draftx", false)]
    [InlineData("0", false)]
    public void ManageJobParams_State_AcceptsOnlyKnownValuesIgnoringCase(string? state, bool valid)
    {
        var parameters = new ManageJobParams { State = state };

        Assert.Equal(valid, Validate(parameters).Count == 0);
    }

    [Fact]
    public void ManageJobParams_InvalidState_HasDescriptiveMessage()
    {
        var error = Assert.Single(Validate(new ManageJobParams { State = "nope" }));

        Assert.Equal("State must be one of: draft, active, expired, closed", error.ErrorMessage);
        Assert.Contains(nameof(ManageJobParams.State), error.MemberNames);
    }

    [Fact]
    public void ApplicationParams_Status_MustBeDefined()
    {
        Assert.Empty(Validate(new ApplicationParams()));
        Assert.Empty(Validate(new ApplicationParams { Status = ApplicationStatus.Withdrawn }));
        Assert.NotEmpty(Validate(new ApplicationParams { Status = (ApplicationStatus)42 }));
    }
}
