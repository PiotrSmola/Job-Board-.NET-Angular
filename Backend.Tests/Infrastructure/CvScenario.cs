using System.Net;
using Backend.DTOs;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Infrastructure;

// Aplikacja kandydata do oferty zatwierdzonej firmy pracodawcy (punkt wyjścia testów CV)
public sealed record CvSetup(TestEmployer Employer, JobOfferDto Job, TestUser Candidate, JobApplicationDto Application);

// Wspólne kroki testów CV: przygotowanie aplikacji, upload, odczyt klucza pliku i zawartości katalogu magazynu Local
public sealed class CvScenario
{
    public const string ApplicationNotFound = "Application not found";
    public const string CvStatusMessage = "CV cannot be changed in the current application status";

    // statusy, w których kandydat może zmieniać CV (JobApplication.CvEditableStatuses)
    public static readonly ApplicationStatus[] EditableStatuses =
        [ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.Interview, ApplicationStatus.Offered];

    public static readonly ApplicationStatus[] LockedStatuses =
        [ApplicationStatus.Hired, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn];

    private readonly ApiFixture _api;

    public CvScenario(ApiFixture api) => _api = api;

    public async Task<CvSetup> NewSetupAsync()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        var candidate = await _api.RegisterUserAsync("cv");
        var application = await _api.ApplyAsync(candidate, job.Id);
        return new CvSetup(employer, job, candidate, application);
    }

    // Kolejny kandydat aplikujący do tej samej oferty
    public async Task<(TestUser Candidate, JobApplicationDto Application)> ApplyAnotherAsync(CvSetup setup)
    {
        var candidate = await _api.RegisterUserAsync("cv");
        return (candidate, await _api.ApplyAsync(candidate, setup.Job.Id));
    }

    public async Task<JobApplicationDto> UploadOkAsync(TestUser user, int applicationId, byte[] content, string fileName = "cv.pdf")
    {
        using var client = _api.CreateClient(user);
        var response = await client.UploadCvAsync(applicationId, content, fileName);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<JobApplicationDto>();
    }

    // Ustawia status aplikacji: Withdrawn przez kandydata, pozostałe przez admina
    public async Task SetStatusAsync(CvSetup setup, ApplicationStatus status) =>
        await SetStatusAsync(setup.Candidate, setup.Application.Id, status);

    public async Task SetStatusAsync(TestUser candidate, int applicationId, ApplicationStatus status)
    {
        if (status == ApplicationStatus.Submitted)
        {
            return;
        }

        if (status == ApplicationStatus.Withdrawn)
        {
            Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(candidate, applicationId)).StatusCode);
            return;
        }

        await _api.SetApplicationStatusAsync(applicationId, status);
    }

    public Task<string?> StoredKeyAsync(int applicationId) =>
        _api.QueryDbAsync(db => db.JobApplications.Where(a => a.Id == applicationId).Select(a => a.CvFileKey).FirstOrDefaultAsync());

    public string StoragePath(string key) => Path.Combine(_api.Factory.StoragePath, key.Replace('/', Path.DirectorySeparatorChar));

    public string[] StoredFilesOf(int applicationId)
    {
        var directory = Path.Combine(_api.Factory.StoragePath, "cv", applicationId.ToString());
        return Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
    }

    // Wszystkie pliki CV w magazynie fabryki (dla wszystkich aplikacji)
    public string[] AllStoredFiles()
    {
        var directory = Path.Combine(_api.Factory.StoragePath, "cv");
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories) : [];
    }

    // Surowe pola CV rekordu - do porównań "stan nie zmienił się"
    public Task<(string? Key, string? Name, string? ContentType, long? Size, DateTime? UploadedAt)> CvRecordAsync(int applicationId) =>
        _api.QueryDbAsync(async db =>
        {
            var a = await db.JobApplications.AsNoTracking().FirstAsync(x => x.Id == applicationId);
            return (a.CvFileKey, a.CvFileName, a.CvContentType, a.CvSize, a.CvUploadedAt);
        });
}
