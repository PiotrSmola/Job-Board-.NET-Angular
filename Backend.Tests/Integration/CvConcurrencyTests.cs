using System.Net;
using System.Net.Http.Headers;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Backend.Repositories;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.Tests.Integration;

// Równoległe zmiany CV tej samej aplikacji. Testy "deterministyczne" podmieniają repozytorium dekoratorem, który
// wstrzymuje żądania po wczytaniu aplikacji (bariera) i opcjonalnie wykonuje akcję innego żądania przed ich zapisem.
// Wspólny warunek: po wyścigu w magazynie jest dokładnie to, na co wskazuje rekord - żadnych osieroconych plików.
[Collection(ApiCollection.Name)]
public class CvConcurrencyTests
{
    private const string Url = "/api/jobapplications";
    private const string ConcurrentChange = "CV was changed by another request";

    private readonly ApiFixture _api;
    private readonly CvScenario _cv;

    public CvConcurrencyTests(ApiFixture api)
    {
        _api = api;
        _cv = new CvScenario(api);
    }

    // Bariera: pierwsze `participants` odczytów aplikacji czeka na wszystkie (z limitem czasu, żeby błąd nie zawiesił testu)
    private sealed class ReadGate
    {
        private readonly int _participants;
        private int _arrived;
        private int _hookRan;
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ReadGate(int participants) => _participants = participants;

        // akcja wykonywana raz, po tym jak wszyscy uczestnicy wczytali aplikację (przed ich zapisem)
        public Func<Task>? AfterAllRead { get; init; }

        public async Task ArriveAsync()
        {
            var number = Interlocked.Increment(ref _arrived);
            if (number > _participants)
            {
                return;
            }

            if (number == _participants)
            {
                if (AfterAllRead != null && Interlocked.Exchange(ref _hookRan, 1) == 0)
                {
                    await AfterAllRead();
                }

                _all.TrySetResult();
            }

            await _all.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    private sealed class GatedRepository : IJobApplicationRepository
    {
        private readonly IJobApplicationRepository _inner;
        private readonly ReadGate _gate;

        public GatedRepository(IJobApplicationRepository inner, ReadGate gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public async Task<JobApplication?> GetApplicationByIdAsync(int id, bool includeHistory = false, CancellationToken cancellationToken = default)
        {
            var application = await _inner.GetApplicationByIdAsync(id, includeHistory, cancellationToken);
            if (!includeHistory)
            {
                await _gate.ArriveAsync();
            }

            return application;
        }

        public Task<JobApplication?> GetApplicationForUpdateAsync(int id) => _inner.GetApplicationForUpdateAsync(id);
        public Task<JobApplication?> GetUserApplicationForJobAsync(int jobId, string userId) => _inner.GetUserApplicationForJobAsync(jobId, userId);
        public Task<IEnumerable<JobApplication>> GetApplicationsByUserIdAsync(string userId) => _inner.GetApplicationsByUserIdAsync(userId);
        public Task<IEnumerable<JobApplication>> GetApplicationsByJobIdAsync(int jobId) => _inner.GetApplicationsByJobIdAsync(jobId);
        public Task<PagedList<JobApplication>> GetApplicationsAsync(ApplicationParams applicationParams, ManagementScope scope) => _inner.GetApplicationsAsync(applicationParams, scope);
        public Task<JobApplication> AddApplicationAsync(JobApplication application) => _inner.AddApplicationAsync(application);
        public Task UpdateApplicationAsync(JobApplication application, Action<DateTime> change) => _inner.UpdateApplicationAsync(application, change);
        public Task<bool> UpdateCvAsync(int id, string userId, string? expectedFileKey, CvFile? cv, CancellationToken cancellationToken = default) =>
            _inner.UpdateCvAsync(id, userId, expectedFileKey, cv, cancellationToken);
        public Task<(bool Deleted, string? CvFileKey)> DeleteApplicationAsync(int id) => _inner.DeleteApplicationAsync(id);
    }

    private WebApplicationFactory<Program> GatedFactory(ReadGate gate) =>
        _api.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IJobApplicationRepository>();
            services.AddScoped<JobApplicationRepository>();
            services.AddScoped<IJobApplicationRepository>(sp =>
                new GatedRepository(sp.GetRequiredService<JobApplicationRepository>(), gate));
        }));

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Magazyn zgodny z rekordem: brak CV = brak plików, inaczej dokładnie jeden plik wskazany przez rekord
    private async Task AssertStorageMatchesRecordAsync(int applicationId)
    {
        var record = await _cv.CvRecordAsync(applicationId);
        var files = _cv.StoredFilesOf(applicationId);
        if (record.Key == null)
        {
            Assert.Empty(files);
            return;
        }

        var file = Assert.Single(files);
        Assert.Equal(Path.GetFullPath(_cv.StoragePath(record.Key)), Path.GetFullPath(file));
        Assert.Equal(record.Size, new FileInfo(file).Length);
    }

    // --- deterministyczne wyścigi ---

    [Fact]
    public async Task TwoFirstUploadsReadingTheSameState_OneWinsOtherGetsConflictAndLeavesNoFile()
    {
        var setup = await _cv.NewSetupAsync();
        var first = CvTestFiles.Pdf(paddingBytes: 100);
        var second = CvTestFiles.Pdf(paddingBytes: 200);
        using var factory = GatedFactory(new ReadGate(2));
        using var client = ClientFor(factory, setup.Candidate.Token);

        var results = await Task.WhenAll(
            client.UploadCvAsync(setup.Application.Id, first, "first.pdf"),
            client.UploadCvAsync(setup.Application.Id, second, "second.pdf"));

        var statuses = results.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, statuses);
        var loser = results.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(ConcurrentChange, await loser.ReadMessageAsync());

        // zwycięzca: jego plik jest w magazynie, rekord pasuje do niego
        var winnerDto = await results.Single(r => r.StatusCode == HttpStatusCode.OK).ReadJsonAsync<JobApplicationDto>();
        var record = await _cv.CvRecordAsync(setup.Application.Id);
        Assert.Equal(winnerDto.CvFileName, record.Name);
        await AssertStorageMatchesRecordAsync(setup.Application.Id);
        var expected = record.Name == "first.pdf" ? first : second;
        Assert.Equal(expected, await File.ReadAllBytesAsync(_cv.StoragePath(record.Key!)));
    }

    [Fact]
    public async Task TwoReplacementsReadingTheSameOldCv_LeaveExactlyOneFileAndNoOrphans()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "old.pdf");
        var oldKey = (await _cv.CvRecordAsync(setup.Application.Id)).Key!;
        using var factory = GatedFactory(new ReadGate(2));
        using var client = ClientFor(factory, setup.Candidate.Token);

        var results = await Task.WhenAll(
            client.UploadCvAsync(setup.Application.Id, CvTestFiles.Docx(), "a.docx"),
            client.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf(paddingBytes: 50), "b.pdf"));

        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, results.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray());
        Assert.False(File.Exists(_cv.StoragePath(oldKey)), "The replaced file must be gone");
        await AssertStorageMatchesRecordAsync(setup.Application.Id);
        Assert.NotEqual(oldKey, (await _cv.CvRecordAsync(setup.Application.Id)).Key);
    }

    [Fact]
    public async Task UploadAndDeleteReadingTheSameCv_OneWinsAndStorageMatchesTheRecord()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "old.pdf");
        using var factory = GatedFactory(new ReadGate(2));
        using var client = ClientFor(factory, setup.Candidate.Token);

        var results = await Task.WhenAll(
            client.UploadCvAsync(setup.Application.Id, CvTestFiles.Docx(), "new.docx"),
            client.DeleteAsync($"{Url}/{setup.Application.Id}/cv"));

        // jedno żądanie zmienia CV, drugie widzi zmieniony klucz pliku
        var codes = results.Select(r => r.StatusCode).ToArray();
        Assert.Contains(HttpStatusCode.Conflict, codes);
        Assert.True(codes.Contains(HttpStatusCode.OK) ^ codes.Contains(HttpStatusCode.NoContent), $"Unexpected {string.Join(",", codes)}");
        await AssertStorageMatchesRecordAsync(setup.Application.Id);
    }

    [Fact]
    public async Task UploadWhileFinalDecisionIsMade_ReturnsBadRequestAndRemovesTheNewFile()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "old.pdf");
        var before = await _cv.CvRecordAsync(setup.Application.Id);
        var gate = new ReadGate(1)
        {
            // rekruter odrzuca kandydata, zanim żądanie zdąży zapisać CV
            AfterAllRead = async () => await _api.SetApplicationStatusAsync(setup.Application.Id, ApplicationStatus.Rejected)
        };
        using var factory = GatedFactory(gate);
        using var client = ClientFor(factory, setup.Candidate.Token);

        var response = await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Docx(), "late.docx");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CvScenario.CvStatusMessage, await response.ReadMessageAsync());
        Assert.Equal(before, await _cv.CvRecordAsync(setup.Application.Id));
        await AssertStorageMatchesRecordAsync(setup.Application.Id);
    }

    [Fact]
    public async Task UploadWhileCandidateWithdraws_ReturnsBadRequestAndKeepsOldFile()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "old.pdf");
        var before = await _cv.CvRecordAsync(setup.Application.Id);
        var gate = new ReadGate(1)
        {
            AfterAllRead = async () =>
                Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(setup.Candidate, setup.Application.Id)).StatusCode)
        };
        using var factory = GatedFactory(gate);
        using var client = ClientFor(factory, setup.Candidate.Token);

        var response = await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Docx(), "late.docx");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await _cv.CvRecordAsync(setup.Application.Id));
        await AssertStorageMatchesRecordAsync(setup.Application.Id);
    }

    [Fact]
    public async Task UploadWhileAdminDeletesTheApplication_ReturnsNotFoundAndLeavesNoOrphan()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "old.pdf");
        var gate = new ReadGate(1)
        {
            AfterAllRead = async () =>
            {
                using var admin = await _api.AdminClientAsync();
                Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);
            }
        };
        using var factory = GatedFactory(gate);
        using var client = ClientFor(factory, setup.Candidate.Token);

        var response = await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Docx(), "late.docx");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(CvScenario.ApplicationNotFound, await response.ReadMessageAsync());
        Assert.Empty(_cv.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task UploadWhileAdminDeletesTheJob_ReturnsNotFoundAndLeavesNoOrphan()
    {
        var setup = await _cv.NewSetupAsync();
        var gate = new ReadGate(1)
        {
            AfterAllRead = async () =>
            {
                using var admin = await _api.AdminClientAsync();
                Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}")).StatusCode);
            }
        };
        using var factory = GatedFactory(gate);
        using var client = ClientFor(factory, setup.Candidate.Token);

        var response = await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf(), "late.pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_cv.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task DeleteWhileAnotherRequestReplacesTheCv_NeverLeavesARecordWithoutItsFile()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "old.pdf");
        var oldKey = (await _cv.CvRecordAsync(setup.Application.Id)).Key!;
        var gate = new ReadGate(1)
        {
            // zanim DELETE zdąży zapisać, inne żądanie wymienia CV
            AfterAllRead = async () => await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Docx(), "winner.docx")
        };
        using var factory = GatedFactory(gate);
        using var client = ClientFor(factory, setup.Candidate.Token);

        var response = await client.DeleteAsync($"{Url}/{setup.Application.Id}/cv");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ConcurrentChange, await response.ReadMessageAsync());
        var record = await _cv.CvRecordAsync(setup.Application.Id);
        Assert.Equal("winner.docx", record.Name);
        Assert.NotEqual(oldKey, record.Key);
        await AssertStorageMatchesRecordAsync(setup.Application.Id);
    }

    // --- prawdziwe równoległe żądania (bez dekoratora) ---

    [Fact]
    public async Task ManyParallelReplacements_AlwaysEndWithOneFileMatchingTheRecord()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "start.pdf");

        for (var round = 0; round < 3; round++)
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
            {
                using var client = _api.CreateClient(setup.Candidate);
                return await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf(paddingBytes: round * 100 + i), $"r{round}-{i}.pdf");
            }));

            Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"Unexpected {r.StatusCode}"));
            Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
            await AssertStorageMatchesRecordAsync(setup.Application.Id);

            // rekord wskazuje na plik jednego ze zwycięskich żądań
            var winners = new List<string>();
            foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.OK))
            {
                winners.Add((await response.ReadJsonAsync<JobApplicationDto>()).CvFileName!);
            }

            Assert.Contains((await _cv.CvRecordAsync(setup.Application.Id)).Name, winners);
        }
    }

    [Fact]
    public async Task ParallelUploadsAndDeletes_KeepStorageConsistentWithTheRecord()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "start.pdf");

        for (var round = 0; round < 3; round++)
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
            {
                using var client = _api.CreateClient(setup.Candidate);
                return i % 2 == 0
                    ? await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf(paddingBytes: i), $"u{round}-{i}.pdf")
                    : await client.DeleteAsync($"{Url}/{setup.Application.Id}/cv");
            }));

            // 404 - DELETE, który trafił na już usunięte CV
            Assert.All(responses, r => Assert.True(
                r.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent or HttpStatusCode.Conflict or HttpStatusCode.NotFound,
                $"Unexpected {r.StatusCode}"));
            await AssertStorageMatchesRecordAsync(setup.Application.Id);
        }
    }

    [Fact]
    public async Task ParallelUploadsToDifferentApplications_DoNotInterfere()
    {
        var setup = await _cv.NewSetupAsync();
        var others = new List<(TestUser Candidate, JobApplicationDto Application)> { (setup.Candidate, setup.Application) };
        for (var i = 0; i < 5; i++)
        {
            others.Add(await _cv.ApplyAnotherAsync(setup));
        }

        var responses = await Task.WhenAll(others.Select(async (o, i) =>
        {
            using var client = _api.CreateClient(o.Candidate);
            return await client.UploadCvAsync(o.Application.Id, CvTestFiles.Pdf(paddingBytes: i * 10), $"cv{i}.pdf");
        }));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        foreach (var (candidate, application) in others)
        {
            await AssertStorageMatchesRecordAsync(application.Id);
        }
    }
}
