using System.Net;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Backend.Repositories;
using Backend.Services;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.Tests.Integration;

// Odporność na awarie magazynu plików i bazy: błąd zapisu / odczytu to 500 z ogólnym komunikatem (globalna obsługa
// wyjątków) i bez zmian w bazie ani osieroconych plików, a błąd sprzątania po zatwierdzonej zmianie (usunięcie pliku)
// nie psuje wyniku operacji. Środowisko Production - bez strony błędów deweloperskich.
public class AttachmentStorageFailureTests : IClassFixture<ProductionRefreshEnabledFixture>
{
    private const string Url = "/api/jobapplications";
    private const string Secret = "disk-full-at-/var/secret/path";

    private readonly ProductionRefreshEnabledFixture _api;
    private readonly AttachmentScenario _attachments;

    public AttachmentStorageFailureTests(ProductionRefreshEnabledFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
    }

    // Prawdziwy magazyn Local z wyłącznikami błędów; FailSaveAfter - liczba udanych zapisów przed błędem
    private sealed class FaultyFileStorage : IFileStorage
    {
        private readonly LocalFileStorage _inner;
        private int _saveAttempts;

        public FaultyFileStorage(string path) => _inner = new LocalFileStorage(path);

        public int? FailSaveAfter { get; set; }
        public bool FailOpen { get; set; }
        public bool FailDelete { get; set; }
        public bool FailDeleteByPrefix { get; set; }
        public int SaveAttempts => _saveAttempts;
        public List<string> DeletedKeys { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);

        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _saveAttempts);
            return attempt > FailSaveAfter ? throw new IOException(Secret) : _inner.SaveAsync(key, content, contentType, cancellationToken);
        }

        public Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            FailOpen ? throw new IOException(Secret) : _inner.OpenReadAsync(key, cancellationToken);

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            lock (DeletedKeys)
            {
                DeletedKeys.Add(key);
            }

            return FailDelete ? throw new IOException(Secret) : _inner.DeleteAsync(key, cancellationToken);
        }

        public Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            FailDeleteByPrefix ? throw new IOException(Secret) : _inner.DeleteByPrefixAsync(prefix, cancellationToken);
    }

    // Repozytorium, którego zapis rekordów załączników kończy się wyjątkiem (awaria bazy po zapisaniu plików):
    // przed zapisem (nic w bazie) albo już po zatwierdzeniu transakcji (rekordy są, np. zerwane połączenie przy
    // potwierdzeniu COMMIT); FailExistenceCheck - nie da się też sprawdzić, które rekordy istnieją
    private sealed class FailingAttachmentsRepository(IJobApplicationRepository inner, bool afterCommit = false,
        bool failExistenceCheck = false) : IJobApplicationRepository
    {
        public async Task<AttachmentsAddResult> AddAttachmentsAsync(int applicationId, string userId,
            IReadOnlyList<ApplicationAttachment> attachments, CancellationToken cancellationToken = default)
        {
            if (afterCommit)
            {
                Assert.Equal(AttachmentsAddResult.Added, await inner.AddAttachmentsAsync(applicationId, userId, attachments, cancellationToken));
            }

            throw new InvalidOperationException(Secret);
        }

        public Task<IReadOnlyList<string>> GetExistingFileKeysAsync(IReadOnlyCollection<string> fileKeys, CancellationToken cancellationToken = default) =>
            failExistenceCheck ? throw new InvalidOperationException(Secret) : inner.GetExistingFileKeysAsync(fileKeys, cancellationToken);

        public Task<ApplicationOwnership?> GetOwnershipAsync(int id, CancellationToken cancellationToken = default) => inner.GetOwnershipAsync(id, cancellationToken);
        public Task<JobApplication?> GetApplicationByIdAsync(int id, bool includeHistory = false, CancellationToken cancellationToken = default) => inner.GetApplicationByIdAsync(id, includeHistory, cancellationToken);
        public Task<JobApplication?> GetApplicationForUpdateAsync(int id) => inner.GetApplicationForUpdateAsync(id);
        public Task<JobApplication?> GetUserApplicationForJobAsync(int jobId, string userId) => inner.GetUserApplicationForJobAsync(jobId, userId);
        public Task<IEnumerable<JobApplication>> GetApplicationsByUserIdAsync(string userId) => inner.GetApplicationsByUserIdAsync(userId);
        public Task<IEnumerable<JobApplication>> GetApplicationsByJobIdAsync(int jobId) => inner.GetApplicationsByJobIdAsync(jobId);
        public Task<PagedList<JobApplication>> GetApplicationsAsync(ApplicationParams applicationParams, ManagementScope scope) => inner.GetApplicationsAsync(applicationParams, scope);
        public Task<JobApplication> AddApplicationAsync(JobApplication application) => inner.AddApplicationAsync(application);
        public Task UpdateApplicationAsync(JobApplication application, Action<DateTime> change) => inner.UpdateApplicationAsync(application, change);
        public Task<(AttachmentDeleteResult Result, string? FileKey)> DeleteAttachmentAsync(int applicationId, int attachmentId, string userId,
            CancellationToken cancellationToken = default) => inner.DeleteAttachmentAsync(applicationId, attachmentId, userId, cancellationToken);
        public Task<(bool Deleted, IReadOnlyList<string> FileKeys)> DeleteApplicationAsync(int id) => inner.DeleteApplicationAsync(id);
    }

    private (WebApplicationFactory<Program> Factory, FaultyFileStorage Storage) FactoryWithFaultyStorage()
    {
        var storage = new FaultyFileStorage(_api.Factory.StoragePath);
        var factory = _api.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage>(storage);
        }));
        return (factory, storage);
    }

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // 500 z ogólnym komunikatem - bez treści, typu ani stosu wyjątku
    private static async Task AssertServerErrorWithoutDetailsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(GlobalExceptionHandler.Message, await response.ReadMessageAsync());
        Assert.DoesNotContain(Secret, body);
        Assert.DoesNotContain("IOException", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("at Backend.", body);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    private static AttachmentUpload[] ThreeFiles() =>
    [
        new(AttachmentTestFiles.Pdf(), "cv.pdf", AttachmentKind.Cv),
        new(AttachmentTestFiles.Docx(), "letter.docx", AttachmentKind.CoverLetter),
        new(AttachmentTestFiles.Png(), "photo.png", AttachmentKind.Portfolio)
    ];

    // --- błędy zapisu i odczytu: 500 i brak zmian ---

    [Fact]
    public async Task StorageFailsOnFirstFile_ServerErrorAndNothingChanges()
    {
        var setup = await _attachments.NewSetupAsync();
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var client = ClientFor(factory, setup.Candidate.Token);
        storage.FailSaveAfter = 0;

        await AssertServerErrorWithoutDetailsAsync(await client.UploadAttachmentsAsync(setup.Application.Id, ThreeFiles()));

        Assert.Equal(1, storage.SaveAttempts);
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));

        // po usunięciu awarii ten sam upload się udaje
        storage.FailSaveAfter = null;
        Assert.Equal(HttpStatusCode.Created, (await client.UploadAttachmentsAsync(setup.Application.Id, ThreeFiles())).StatusCode);
    }

    [Fact]
    public async Task StorageFailsOnTheThirdFile_AlreadySavedFilesAreRemovedAndOldAttachmentsStay()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(paddingBytes: 7), "old.pdf");
        var before = await _attachments.RecordsAsync(setup.Application.Id);
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var client = ClientFor(factory, setup.Candidate.Token);
        storage.FailSaveAfter = 2;

        await AssertServerErrorWithoutDetailsAsync(await client.UploadAttachmentsAsync(setup.Application.Id, ThreeFiles()));

        // wszystko albo nic: dwa zapisane pliki (i klucz trzeciego) posprzątane, stary załącznik bez zmian
        Assert.Equal(3, storage.SaveAttempts);
        Assert.Equal(3, storage.DeletedKeys.Count);
        Assert.Equal(before, await _attachments.RecordsAsync(setup.Application.Id));
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
        storage.FailSaveAfter = null;
        var download = await client.DownloadAttachmentAsync(setup.Application.Id, before.Single().Id);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(AttachmentTestFiles.Pdf(paddingBytes: 7), await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task DatabaseFailsAfterTheFilesWereSaved_NewFilesAreRemovedAndOldAttachmentsStay()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "old.pdf");
        var before = await _attachments.RecordsAsync(setup.Application.Id);
        using var factory = _api.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IJobApplicationRepository>();
            services.AddScoped<JobApplicationRepository>();
            services.AddScoped<IJobApplicationRepository>(sp => new FailingAttachmentsRepository(sp.GetRequiredService<JobApplicationRepository>()));
        }));
        using var client = ClientFor(factory, setup.Candidate.Token);

        await AssertServerErrorWithoutDetailsAsync(await client.UploadAttachmentsAsync(setup.Application.Id, ThreeFiles()));

        // pliki zapisane tuż przed błędem bazy zostały posprzątane, stary załącznik bez zmian
        Assert.Equal(before, await _attachments.RecordsAsync(setup.Application.Id));
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    private WebApplicationFactory<Program> FactoryWithFailingAttachmentsRepository(bool afterCommit, bool failExistenceCheck) =>
        _api.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IJobApplicationRepository>();
            services.AddScoped<JobApplicationRepository>();
            services.AddScoped<IJobApplicationRepository>(sp => new FailingAttachmentsRepository(
                sp.GetRequiredService<JobApplicationRepository>(), afterCommit, failExistenceCheck));
        }));

    // Błąd po zatwierdzeniu transakcji: rekordy są w bazie, więc ich pliki zostają (inaczej rekordy wskazywałyby
    // na nieistniejące pliki) - klient dostaje 500, ale załączniki są kompletne i dają się pobrać
    [Fact]
    public async Task DatabaseFailsAfterTheCommit_FilesOfTheCommittedRecordsAreKept()
    {
        var setup = await _attachments.NewSetupAsync();
        using var factory = FactoryWithFailingAttachmentsRepository(afterCommit: true, failExistenceCheck: false);
        using var client = ClientFor(factory, setup.Candidate.Token);

        await AssertServerErrorWithoutDetailsAsync(await client.UploadAttachmentsAsync(setup.Application.Id, ThreeFiles()));

        var records = await _attachments.RecordsAsync(setup.Application.Id);
        Assert.Equal(3, records.Count);
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
        foreach (var (record, expected) in records.OrderBy(r => r.Kind).Zip(ThreeFiles()))
        {
            var download = await client.DownloadAttachmentAsync(setup.Application.Id, record.Id);
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(expected.Content, await download.Content.ReadAsByteArrayAsync());
        }
    }

    // Nie da się sprawdzić, które rekordy powstały - pliki zostają (osierocony plik zamiast rekordu bez pliku)
    [Fact]
    public async Task DatabaseFailsAndTheRecordCheckFailsToo_FilesAreKept()
    {
        var setup = await _attachments.NewSetupAsync();
        using var factory = FactoryWithFailingAttachmentsRepository(afterCommit: true, failExistenceCheck: true);
        using var client = ClientFor(factory, setup.Candidate.Token);

        await AssertServerErrorWithoutDetailsAsync(await client.UploadAttachmentsAsync(setup.Application.Id, ThreeFiles()));

        Assert.Equal(3, (await _attachments.RecordsAsync(setup.Application.Id)).Count);
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Fact]
    public async Task StorageFailsOnDownload_ServerErrorAndRecordUnchanged()
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "cv.pdf");
        var before = await _attachments.RecordsAsync(setup.Application.Id);
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var client = ClientFor(factory, setup.Candidate.Token);
        storage.FailOpen = true;

        await AssertServerErrorWithoutDetailsAsync(await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id));

        Assert.Equal(before, await _attachments.RecordsAsync(setup.Application.Id));
        storage.FailOpen = false;
        Assert.Equal(HttpStatusCode.OK, (await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id)).StatusCode);
    }

    // --- błędy sprzątania po zatwierdzonej zmianie: operacja się udaje ---

    [Fact]
    public async Task StorageFailsOnDeletingAnAttachmentFile_RequestSucceedsAndRecordIsRemoved()
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "cv.pdf");
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var client = ClientFor(factory, setup.Candidate.Token);
        storage.FailDelete = true;

        var response = await client.DeleteAttachmentAsync(setup.Application.Id, attachment.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id)).StatusCode);
    }

    [Fact]
    public async Task StorageFailsOnCleanupAfterARejectedUpload_ResponseStillReportsTheRejection()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            Enumerable.Range(0, AttachmentFiles.MaxAttachments).Select(i => new AttachmentUpload(AttachmentTestFiles.Pdf(i), $"a{i}.pdf")).ToArray());
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var client = ClientFor(factory, setup.Candidate.Token);
        storage.FailDelete = true;

        // limit sprawdzany pod blokadą po zapisaniu pliku - nieudane sprzątanie jest tylko logowane
        var response = await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf(), "eleventh.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.TooManyMessage, await response.ReadMessageAsync());
        Assert.Single(storage.DeletedKeys);
        Assert.Equal(AttachmentFiles.MaxAttachments, (await _attachments.RecordsAsync(setup.Application.Id)).Count);
    }

    [Fact]
    public async Task StorageFailsOnDeletingFilesOfRemovedApplicationAndJob_RequestsStillSucceed()
    {
        var setup = await _attachments.NewSetupAsync();
        var (other, otherApplication) = await _attachments.ApplyAnotherAsync(setup);
        await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id, ThreeFiles());
        await _attachments.UploadOkAsync(other, otherApplication.Id, AttachmentTestFiles.Pdf(), "b.pdf");
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var admin = ClientFor(factory, await _api.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword));
        storage.FailDelete = true;

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}")).StatusCode);

        // próba usunięcia każdego pliku mimo błędów poprzednich
        Assert.Equal(4, storage.DeletedKeys.Count);
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Empty(await _attachments.RecordsAsync(otherApplication.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Url}/{otherApplication.Id}")).StatusCode);
    }

    [Fact]
    public async Task StorageFailsOnSeedRefresh_RefreshStillSucceeds()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "cv.pdf");
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var admin = ClientFor(factory, await _api.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword));
        storage.FailDeleteByPrefix = true;

        var response = await admin.PostAsync("/api/admin/refresh-seed", null);

        // dane w bazie zostały zresetowane; błąd magazynu jest tylko logowany, a odpowiedź nie zdradza jego treści
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(Secret, await response.Content.ReadAsStringAsync());
        Assert.False(await _api.QueryDbAsync(db => db.JobApplications.AnyAsync(a => a.Id == setup.Application.Id)));
    }

    [Fact]
    public async Task ValidationErrors_DoNotTouchTheStorageAtAll()
    {
        var setup = await _attachments.NewSetupAsync();
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var client = ClientFor(factory, setup.Candidate.Token);
        storage.FailSaveAfter = 0;
        storage.FailDelete = true;

        // odrzucony typ, pusty plik, zły rodzaj, za dużo plików, obcy użytkownik - błąd klienta, zanim dojdzie do magazynu
        Assert.Equal(HttpStatusCode.BadRequest, (await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Gif(), "x.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.UploadAttachmentAsync(setup.Application.Id, [], "x.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.UploadAttachmentsAsync(setup.Application.Id,
            new AttachmentUpload(AttachmentTestFiles.Pdf(), "ok.pdf"), new AttachmentUpload(AttachmentTestFiles.Gif(), "bad.png"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.UploadAttachmentsAsync(setup.Application.Id,
            Enumerable.Range(0, 11).Select(i => new AttachmentUpload(AttachmentTestFiles.Pdf(i), $"{i}.pdf")).ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.UploadAttachmentAsync(int.MaxValue, AttachmentTestFiles.Pdf())).StatusCode);

        Assert.Equal(0, storage.SaveAttempts);
        Assert.Empty(storage.DeletedKeys);
    }
}
