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

// Odporność na awarie magazynu plików i bazy: błąd zapisu / odczytu to 500 bez szczegółów i bez zmian w bazie,
// a błąd sprzątania po zatwierdzonej zmianie (usunięcie pliku) nie psuje wyniku operacji.
// Środowisko Production (bez strony błędów deweloperskich): TestServer przekazuje wtedy niezłapany wyjątek
// wywołującemu (Kestrel zwróciłby pustą odpowiedź 500) - oba przypadki traktujemy jako błąd serwera bez treści z wyjątku.
public class CvStorageFailureTests : IClassFixture<ProductionRefreshEnabledFixture>
{
    private const string Url = "/api/jobapplications";
    private const string Secret = "disk-full-at-/var/secret/path";

    private readonly ProductionRefreshEnabledFixture _api;
    private readonly CvScenario _cv;

    public CvStorageFailureTests(ProductionRefreshEnabledFixture api)
    {
        _api = api;
        _cv = new CvScenario(api);
    }

    // Prawdziwy magazyn Local z wyłącznikami błędów
    private sealed class FaultyFileStorage : IFileStorage
    {
        private readonly LocalFileStorage _inner;

        public FaultyFileStorage(string path) => _inner = new LocalFileStorage(path);

        public bool FailSave { get; set; }
        public bool FailOpen { get; set; }
        public bool FailDelete { get; set; }
        public bool FailDeleteByPrefix { get; set; }
        public int SaveAttempts { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);

        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            return FailSave ? throw new IOException(Secret) : _inner.SaveAsync(key, content, contentType, cancellationToken);
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            FailOpen ? throw new IOException(Secret) : _inner.OpenReadAsync(key, cancellationToken);

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            FailDelete ? throw new IOException(Secret) : _inner.DeleteAsync(key, cancellationToken);

        public Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            FailDeleteByPrefix ? throw new IOException(Secret) : _inner.DeleteByPrefixAsync(prefix, cancellationToken);
    }

    // Repozytorium, którego zapis CV się nie udaje (awaria bazy po zapisaniu pliku)
    private sealed class FailingCvRepository : IJobApplicationRepository
    {
        private readonly IJobApplicationRepository _inner;

        public FailingCvRepository(IJobApplicationRepository inner) => _inner = inner;

        public Task<bool> UpdateCvAsync(int id, string userId, string? expectedFileKey, CvFile? cv, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Secret);

        public Task<JobApplication?> GetApplicationByIdAsync(int id, bool includeHistory = false, CancellationToken cancellationToken = default) => _inner.GetApplicationByIdAsync(id, includeHistory, cancellationToken);
        public Task<JobApplication?> GetApplicationForUpdateAsync(int id) => _inner.GetApplicationForUpdateAsync(id);
        public Task<JobApplication?> GetUserApplicationForJobAsync(int jobId, string userId) => _inner.GetUserApplicationForJobAsync(jobId, userId);
        public Task<IEnumerable<JobApplication>> GetApplicationsByUserIdAsync(string userId) => _inner.GetApplicationsByUserIdAsync(userId);
        public Task<IEnumerable<JobApplication>> GetApplicationsByJobIdAsync(int jobId) => _inner.GetApplicationsByJobIdAsync(jobId);
        public Task<PagedList<JobApplication>> GetApplicationsAsync(ApplicationParams applicationParams, ManagementScope scope) => _inner.GetApplicationsAsync(applicationParams, scope);
        public Task<JobApplication> AddApplicationAsync(JobApplication application) => _inner.AddApplicationAsync(application);
        public Task UpdateApplicationAsync(JobApplication application, Action<DateTime> change) => _inner.UpdateApplicationAsync(application, change);
        public Task<(bool Deleted, string? CvFileKey)> DeleteApplicationAsync(int id) => _inner.DeleteApplicationAsync(id);
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

    // Wynik żądania, które mogło zakończyć się wyjątkiem zgłoszonym przez TestServer
    private static async Task AssertServerErrorWithoutDetailsAsync(Func<Task<HttpResponseMessage>> send)
    {
        HttpResponseMessage? response = null;
        Exception? thrown = null;
        try
        {
            response = await send();
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        if (response != null)
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(Secret, body);
            Assert.DoesNotContain("IOException", body);
            Assert.DoesNotContain("InvalidOperationException", body);
            Assert.DoesNotContain("at Backend.", body);
            Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
            return;
        }

        // wyjątek wstrzyknięty do magazynu / repozytorium dotarł do hosta - w Kestrel to pusty 500
        Assert.NotNull(thrown);
        Assert.Contains(Secret, thrown!.ToString());
    }

    // --- błędy zapisu i odczytu: 500 i brak zmian ---

    [Fact]
    public async Task StorageFailsOnFirstUpload_ServerErrorAndNothingChanges()
    {
        var setup = await _cv.NewSetupAsync();
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var client = ClientFor(factory, setup.Candidate.Token);
        storage.FailSave = true;

        await AssertServerErrorWithoutDetailsAsync(() => client.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf"));

        Assert.Equal(1, storage.SaveAttempts);
        Assert.Null(await _cv.StoredKeyAsync(setup.Application.Id));
        var record = await _cv.CvRecordAsync(setup.Application.Id);
        Assert.Null(record.Key);
        Assert.Null(record.Name);
        Assert.Null(record.ContentType);
        Assert.Null(record.Size);
        Assert.Null(record.UploadedAt);
        Assert.Empty(_cv.StoredFilesOf(setup.Application.Id));

        // po usunięciu awarii ten sam upload się udaje
        storage.FailSave = false;
        Assert.Equal(HttpStatusCode.OK, (await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf")).StatusCode);
    }

    [Fact]
    public async Task StorageFailsOnReplacement_OldCvAndFileStayIntact()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(paddingBytes: 7), "old.pdf");
        var before = await _cv.CvRecordAsync(setup.Application.Id);
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var client = ClientFor(factory, setup.Candidate.Token);
        storage.FailSave = true;

        await AssertServerErrorWithoutDetailsAsync(() => client.UploadCvAsync(setup.Application.Id, CvTestFiles.Docx(), "new.docx"));

        Assert.Equal(before, await _cv.CvRecordAsync(setup.Application.Id));
        Assert.True(File.Exists(_cv.StoragePath(before.Key!)));
        Assert.Single(_cv.StoredFilesOf(setup.Application.Id));
        storage.FailSave = false;
        var download = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(CvTestFiles.Pdf(paddingBytes: 7), await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task DatabaseFailsAfterTheFileWasSaved_NewFileIsRemovedAndOldCvStays()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "old.pdf");
        var before = await _cv.CvRecordAsync(setup.Application.Id);
        using var factory = _api.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IJobApplicationRepository>();
            services.AddScoped<JobApplicationRepository>();
            services.AddScoped<IJobApplicationRepository>(sp => new FailingCvRepository(sp.GetRequiredService<JobApplicationRepository>()));
        }));
        using var client = ClientFor(factory, setup.Candidate.Token);

        await AssertServerErrorWithoutDetailsAsync(() => client.UploadCvAsync(setup.Application.Id, CvTestFiles.Docx(), "new.docx"));

        // plik zapisany tuż przed błędem bazy został posprzątany, stary CV bez zmian
        Assert.Equal(before, await _cv.CvRecordAsync(setup.Application.Id));
        Assert.Single(_cv.StoredFilesOf(setup.Application.Id));
        Assert.True(File.Exists(_cv.StoragePath(before.Key!)));
    }

    [Fact]
    public async Task StorageFailsOnDownload_ServerErrorAndRecordUnchanged()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");
        var before = await _cv.CvRecordAsync(setup.Application.Id);
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var client = ClientFor(factory, setup.Candidate.Token);
        storage.FailOpen = true;

        await AssertServerErrorWithoutDetailsAsync(() => client.GetAsync($"{Url}/{setup.Application.Id}/cv"));

        Assert.Equal(before, await _cv.CvRecordAsync(setup.Application.Id));
        storage.FailOpen = false;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);
    }

    // --- błędy sprzątania po zatwierdzonej zmianie: operacja się udaje ---

    [Fact]
    public async Task StorageFailsOnDeletingCv_RequestSucceedsAndRecordIsCleared()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var client = ClientFor(factory, setup.Candidate.Token);
        storage.FailDelete = true;

        var response = await client.DeleteAsync($"{Url}/{setup.Application.Id}/cv");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await _cv.StoredKeyAsync(setup.Application.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);
    }

    [Fact]
    public async Task StorageFailsOnDeletingTheReplacedFile_ReplacementStillSucceeds()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "old.pdf");
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var client = ClientFor(factory, setup.Candidate.Token);
        storage.FailDelete = true;

        var response = await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Docx(), "new.docx");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.ReadJsonAsync<JobApplicationDto>();
        Assert.Equal("new.docx", dto.CvFileName);
        var record = await _cv.CvRecordAsync(dto.Id);
        Assert.True(File.Exists(_cv.StoragePath(record.Key!)));
        var download = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");
        Assert.Equal(CvTestFiles.DocxContentType, download.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task StorageFailsOnDeletingFilesOfRemovedApplicationAndJob_RequestsStillSucceed()
    {
        var setup = await _cv.NewSetupAsync();
        var (other, otherApplication) = await _cv.ApplyAnotherAsync(setup);
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "a.pdf");
        await _cv.UploadOkAsync(other, otherApplication.Id, CvTestFiles.Pdf(), "b.pdf");
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var admin = ClientFor(factory, await _api.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword));
        storage.FailDelete = true;

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}")).StatusCode);

        Assert.Null(await _cv.StoredKeyAsync(setup.Application.Id));
        Assert.Null(await _cv.StoredKeyAsync(otherApplication.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Url}/{otherApplication.Id}")).StatusCode);
    }

    [Fact]
    public async Task StorageFailsOnSeedRefresh_RefreshStillSucceeds()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");
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
        var setup = await _cv.NewSetupAsync();
        var (factory, storage) = FactoryWithFaultyStorage();
        using var _ = factory;
        using var client = ClientFor(factory, setup.Candidate.Token);
        storage.FailSave = true;
        storage.FailDelete = true;

        // odrzucony typ, pusty plik i obcy użytkownik kończą się błędem klienta, zanim dojdzie do magazynu
        Assert.Equal(HttpStatusCode.BadRequest, (await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Png(), "x.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.UploadCvAsync(setup.Application.Id, [], "x.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.UploadCvAsync(int.MaxValue, CvTestFiles.Pdf())).StatusCode);

        Assert.Equal(0, storage.SaveAttempts);
    }
}
