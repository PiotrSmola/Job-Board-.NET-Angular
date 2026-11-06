using System.Net;
using Backend.DTOs;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Infrastructure;

// Aplikacja kandydata do oferty zatwierdzonej firmy pracodawcy (punkt wyjścia testów załączników)
public sealed record AttachmentSetup(TestEmployer Employer, JobOfferDto Job, TestUser Candidate, JobApplicationDto Application);

// Rekord załącznika w bazie (z kluczem pliku i content type, których API nie zwraca)
public sealed record AttachmentRecord(int Id, AttachmentKind Kind, string Key, string Name, string ContentType, long Size, DateTime UploadedAt);

// Wspólne kroki testów załączników: przygotowanie aplikacji, upload, odczyt rekordów i zawartości magazynu Local
public sealed class AttachmentScenario
{
    public const string ApplicationNotFound = "Application not found";
    public const string AttachmentNotFound = "Attachment not found";
    public const string StatusMessage = "Attachments cannot be added in the current application status";

    // statusy, w których kandydat może dodawać załączniki (JobApplication.AttachmentUploadStatuses)
    public static readonly ApplicationStatus[] UploadStatuses =
        [ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.Interview, ApplicationStatus.Offered];

    public static readonly ApplicationStatus[] LockedStatuses =
        [ApplicationStatus.Hired, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn];

    private readonly ApiFixture _api;

    public AttachmentScenario(ApiFixture api) => _api = api;

    public async Task<AttachmentSetup> NewSetupAsync()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        var candidate = await _api.RegisterUserAsync("att");
        var application = await _api.ApplyAsync(candidate, job.Id);
        return new AttachmentSetup(employer, job, candidate, application);
    }

    // Kolejny kandydat aplikujący do tej samej oferty
    public async Task<(TestUser Candidate, JobApplicationDto Application)> ApplyAnotherAsync(AttachmentSetup setup)
    {
        var candidate = await _api.RegisterUserAsync("att");
        return (candidate, await _api.ApplyAsync(candidate, setup.Job.Id));
    }

    public async Task<JobApplicationDto> UploadOkAsync(TestUser user, int applicationId, byte[] content, string fileName = "cv.pdf",
        AttachmentKind? kind = null) =>
        await UploadManyOkAsync(user, applicationId, new AttachmentUpload(content, fileName, kind));

    public async Task<JobApplicationDto> UploadManyOkAsync(TestUser user, int applicationId, params AttachmentUpload[] uploads)
    {
        using var client = _api.CreateClient(user);
        var response = await client.UploadAttachmentsAsync(applicationId, uploads);
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Upload failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.ReadJsonAsync<JobApplicationDto>();
    }

    // Upload jednego pliku i id nowego załącznika (najnowszy rekord tej aplikacji)
    public async Task<AttachmentRecord> UploadOneAsync(TestUser user, int applicationId, byte[] content, string fileName = "cv.pdf",
        AttachmentKind? kind = null)
    {
        var before = (await RecordsAsync(applicationId)).Select(r => r.Id).ToHashSet();
        await UploadOkAsync(user, applicationId, content, fileName, kind);
        return Assert.Single(await RecordsAsync(applicationId), r => !before.Contains(r.Id));
    }

    // Ustawia status aplikacji: Withdrawn przez kandydata, pozostałe przez admina
    public async Task SetStatusAsync(AttachmentSetup setup, ApplicationStatus status) =>
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

    // Rekordy załączników aplikacji (wg Id)
    public Task<List<AttachmentRecord>> RecordsAsync(int applicationId) =>
        _api.QueryDbAsync(db => db.ApplicationAttachments.AsNoTracking()
            .Where(a => a.JobApplicationId == applicationId)
            .OrderBy(a => a.Id)
            .Select(a => new AttachmentRecord(a.Id, a.Kind, a.FileKey, a.FileName, a.ContentType, a.Size, a.UploadedAt))
            .ToListAsync());

    public async Task<List<string>> StoredKeysAsync(int applicationId) =>
        (await RecordsAsync(applicationId)).Select(r => r.Key).ToList();

    public string StoragePath(string key) => Path.Combine(_api.Factory.StoragePath, key.Replace('/', Path.DirectorySeparatorChar));

    public string ApplicationDirectory(int applicationId) =>
        Path.Combine(_api.Factory.StoragePath, "attachments", applicationId.ToString());

    public string[] StoredFilesOf(int applicationId)
    {
        var directory = ApplicationDirectory(applicationId);
        return Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
    }

    // Wszystkie pliki załączników w magazynie fabryki (dla wszystkich aplikacji)
    public string[] AllStoredFiles()
    {
        var directory = Path.Combine(_api.Factory.StoragePath, "attachments");
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories) : [];
    }

    // Magazyn zgodny z bazą: dokładnie pliki wskazane przez rekordy aplikacji (bez osieroconych), z rozmiarami z rekordów
    public async Task AssertStorageMatchesRecordsAsync(int applicationId)
    {
        var records = await RecordsAsync(applicationId);
        var expected = records.Select(r => Path.GetFullPath(StoragePath(r.Key))).Order().ToArray();
        var actual = StoredFilesOf(applicationId).Select(Path.GetFullPath).Order().ToArray();

        Assert.Equal(expected, actual);
        foreach (var record in records)
        {
            Assert.Equal(record.Size, new FileInfo(StoragePath(record.Key)).Length);
        }
    }
}
