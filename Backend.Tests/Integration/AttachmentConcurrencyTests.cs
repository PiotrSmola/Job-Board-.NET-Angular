using System.Net;
using System.Net.Http.Headers;
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

// Równoległe zmiany załączników tej samej aplikacji. Testy "deterministyczne" podmieniają repozytorium dekoratorem,
// który wstrzymuje żądania w wybranym punkcie (bariera) i opcjonalnie wykonuje akcję innego żądania przed ich dalszym
// ciągiem. Wspólne warunki: limity 10 plików i 25 MB nie są przekraczane nawet pod współbieżnością, a w magazynie jest
// dokładnie to, na co wskazują rekordy - żadnych osieroconych plików.
[Collection(ApiCollection.Name)]
public class AttachmentConcurrencyTests
{
    private readonly ApiFixture _api;
    private readonly AttachmentScenario _attachments;

    public AttachmentConcurrencyTests(ApiFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
    }

    // Bariera: pierwszych `participants` wywołań czeka na wszystkie (z limitem czasu, żeby błąd nie zawiesił testu)
    private sealed class Gate
    {
        private readonly int _participants;
        private int _arrived;
        private int _hookRan;
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Gate(int participants) => _participants = participants;

        // akcja wykonywana raz, gdy wszyscy uczestnicy dotarli do bariery (przed ich dalszym ciągiem)
        public Func<Task>? AfterAllArrived { get; init; }

        public async Task ArriveAsync()
        {
            var number = Interlocked.Increment(ref _arrived);
            if (number > _participants)
            {
                return;
            }

            if (number == _participants)
            {
                if (AfterAllArrived != null && Interlocked.Exchange(ref _hookRan, 1) == 0)
                {
                    await AfterAllArrived();
                }

                _all.TrySetResult();
            }

            await _all.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    // OwnershipGate - po lekkim sprawdzeniu właściciela (przed odczytem formularza i zapisem plików);
    // AddGate - po zapisaniu plików w magazynie, przed transakcją z blokadą
    private sealed class GatedRepository(IJobApplicationRepository inner, Gate? ownershipGate, Gate? addGate) : IJobApplicationRepository
    {
        public async Task<ApplicationOwnership?> GetOwnershipAsync(int id, CancellationToken cancellationToken = default)
        {
            var ownership = await inner.GetOwnershipAsync(id, cancellationToken);
            if (ownershipGate != null)
            {
                await ownershipGate.ArriveAsync();
            }

            return ownership;
        }

        public async Task<AttachmentsAddResult> AddAttachmentsAsync(int applicationId, string userId,
            IReadOnlyList<ApplicationAttachment> attachments, CancellationToken cancellationToken = default)
        {
            if (addGate != null)
            {
                await addGate.ArriveAsync();
            }

            return await inner.AddAttachmentsAsync(applicationId, userId, attachments, cancellationToken);
        }

        public Task<JobApplication?> GetApplicationByIdAsync(int id, bool includeHistory = false, CancellationToken cancellationToken = default) =>
            inner.GetApplicationByIdAsync(id, includeHistory, cancellationToken);
        public Task<JobApplication?> GetApplicationForUpdateAsync(int id) => inner.GetApplicationForUpdateAsync(id);
        public Task<JobApplication?> GetUserApplicationForJobAsync(int jobId, string userId) => inner.GetUserApplicationForJobAsync(jobId, userId);
        public Task<IEnumerable<JobApplication>> GetApplicationsByUserIdAsync(string userId) => inner.GetApplicationsByUserIdAsync(userId);
        public Task<PagedList<JobApplication>> GetApplicationsAsync(ApplicationParams applicationParams, ManagementScope scope) => inner.GetApplicationsAsync(applicationParams, scope);
        public Task<JobApplication> AddApplicationAsync(JobApplication application) => inner.AddApplicationAsync(application);
        public Task UpdateApplicationAsync(JobApplication application, Action<DateTime> change) => inner.UpdateApplicationAsync(application, change);
        public Task<(AttachmentDeleteResult Result, string? FileKey)> DeleteAttachmentAsync(int applicationId, int attachmentId, string userId,
            CancellationToken cancellationToken = default) => inner.DeleteAttachmentAsync(applicationId, attachmentId, userId, cancellationToken);
        public Task<(bool Deleted, IReadOnlyList<string> FileKeys)> DeleteApplicationAsync(int id) => inner.DeleteApplicationAsync(id);
        public Task<IReadOnlyList<string>> GetExistingFileKeysAsync(IReadOnlyCollection<string> fileKeys, CancellationToken cancellationToken = default) =>
            inner.GetExistingFileKeysAsync(fileKeys, cancellationToken);
    }

    private WebApplicationFactory<Program> GatedFactory(Gate? ownershipGate = null, Gate? addGate = null) =>
        _api.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IJobApplicationRepository>();
            services.AddScoped<JobApplicationRepository>();
            services.AddScoped<IJobApplicationRepository>(sp =>
                new GatedRepository(sp.GetRequiredService<JobApplicationRepository>(), ownershipGate, addGate));
        }));

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static AttachmentUpload Pdf(int padding, string name) => new(AttachmentTestFiles.Pdf(padding), name);

    private static AttachmentUpload PdfOfSize(long size, string name) => new(AttachmentTestFiles.PdfOfSize(size), name);

    private static HttpStatusCode[] Sorted(IEnumerable<HttpResponseMessage> responses) =>
        responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();

    // --- deterministyczne wyścigi o limity ---

    [Fact]
    public async Task TwoUploadsFillingTheLastSlot_OneWinsTheOtherGetsTooManyAndLeavesNoFile()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            Enumerable.Range(0, AttachmentFiles.MaxAttachments - 1).Select(i => Pdf(i, $"old{i}.pdf")).ToArray());
        // oba żądania zapisują pliki, zanim którekolwiek zablokuje aplikację
        using var factory = GatedFactory(addGate: new Gate(2));
        using var client = ClientFor(factory, setup.Candidate.Token);

        var results = await Task.WhenAll(
            client.UploadAttachmentsAsync(setup.Application.Id, Pdf(100, "first.pdf")),
            client.UploadAttachmentsAsync(setup.Application.Id, Pdf(200, "second.pdf")));

        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest }, Sorted(results));
        Assert.Equal(AttachmentFiles.TooManyMessage, await results.Single(r => r.StatusCode == HttpStatusCode.BadRequest).ReadMessageAsync());
        Assert.Equal(AttachmentFiles.MaxAttachments, (await _attachments.RecordsAsync(setup.Application.Id)).Count);
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Fact]
    public async Task TwoUploadsTogetherExceeding25Mb_OneWinsTheOtherGetsTotalTooLarge()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            Enumerable.Range(0, 4).Select(i => PdfOfSize(AttachmentFiles.MaxFileSize - i, $"old{i}.pdf")).ToArray());
        using var factory = GatedFactory(addGate: new Gate(2));
        using var client = ClientFor(factory, setup.Candidate.Token);

        // ~20 MB istniejących + 3 MB mieści się, + 2 x 3 MB już nie
        var results = await Task.WhenAll(
            client.UploadAttachmentsAsync(setup.Application.Id, PdfOfSize(3 * 1024 * 1024, "a.pdf")),
            client.UploadAttachmentsAsync(setup.Application.Id, PdfOfSize(3 * 1024 * 1024 + 1, "b.pdf")));

        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest }, Sorted(results));
        Assert.Equal(AttachmentFiles.TotalTooLargeMessage, await results.Single(r => r.StatusCode == HttpStatusCode.BadRequest).ReadMessageAsync());
        Assert.True((await _attachments.RecordsAsync(setup.Application.Id)).Sum(r => r.Size) <= AttachmentFiles.MaxTotalSize);
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Fact]
    public async Task UploadWhileFinalDecisionIsMade_ReturnsBadRequestAndRemovesTheNewFiles()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "old.pdf");
        var before = await _attachments.RecordsAsync(setup.Application.Id);
        var gate = new Gate(1)
        {
            // rekruter odrzuca kandydata po zapisaniu plików, zanim żądanie zapisze rekordy
            AfterAllArrived = async () => await _api.SetApplicationStatusAsync(setup.Application.Id, ApplicationStatus.Rejected)
        };
        using var factory = GatedFactory(addGate: gate);
        using var client = ClientFor(factory, setup.Candidate.Token);

        var response = await client.UploadAttachmentsAsync(setup.Application.Id, Pdf(1, "late1.pdf"), new AttachmentUpload(AttachmentTestFiles.Docx(), "late2.docx"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentScenario.StatusMessage, await response.ReadMessageAsync());
        Assert.Equal(before, await _attachments.RecordsAsync(setup.Application.Id));
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Fact]
    public async Task UploadWhileCandidateWithdraws_ReturnsBadRequestAndKeepsOldFiles()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "old.pdf");
        var before = await _attachments.RecordsAsync(setup.Application.Id);
        var gate = new Gate(1)
        {
            AfterAllArrived = async () =>
                Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(setup.Candidate, setup.Application.Id)).StatusCode)
        };
        using var factory = GatedFactory(ownershipGate: gate);
        using var client = ClientFor(factory, setup.Candidate.Token);

        var response = await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Docx(), "late.docx");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await _attachments.RecordsAsync(setup.Application.Id));
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UploadWhileAdminDeletesTheApplication_ReturnsNotFoundAndLeavesNoOrphan(bool afterFilesWereSaved)
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "old.pdf");
        var gate = new Gate(1)
        {
            AfterAllArrived = async () =>
            {
                using var admin = await _api.AdminClientAsync();
                Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/jobapplications/{setup.Application.Id}")).StatusCode);
            }
        };
        using var factory = afterFilesWereSaved ? GatedFactory(addGate: gate) : GatedFactory(ownershipGate: gate);
        using var client = ClientFor(factory, setup.Candidate.Token);

        var response = await client.UploadAttachmentsAsync(setup.Application.Id, Pdf(1, "late.pdf"), new AttachmentUpload(AttachmentTestFiles.Png(), "late.png"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(AttachmentScenario.ApplicationNotFound, await response.ReadMessageAsync());
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
        Assert.False(Directory.Exists(_attachments.ApplicationDirectory(setup.Application.Id)));
    }

    [Fact]
    public async Task UploadWhileAdminDeletesTheJob_ReturnsNotFoundAndLeavesNoOrphan()
    {
        var setup = await _attachments.NewSetupAsync();
        var gate = new Gate(1)
        {
            AfterAllArrived = async () =>
            {
                using var admin = await _api.AdminClientAsync();
                Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}")).StatusCode);
            }
        };
        using var factory = GatedFactory(addGate: gate);
        using var client = ClientFor(factory, setup.Candidate.Token);

        var response = await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf(), "late.pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    // --- prawdziwe równoległe żądania (bez dekoratora) ---

    [Fact]
    public async Task ManyParallelSingleFileUploads_NeverExceedTenAttachments()
    {
        var setup = await _attachments.NewSetupAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 16).Select(async i =>
        {
            using var client = _api.CreateClient(setup.Candidate);
            return await client.UploadAttachmentsAsync(setup.Application.Id, Pdf(i, $"p{i}.pdf"));
        }));

        Assert.Equal(AttachmentFiles.MaxAttachments, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(AttachmentFiles.TooManyMessage, await rejected.ReadMessageAsync());
        }

        Assert.Equal(AttachmentFiles.MaxAttachments, (await _attachments.RecordsAsync(setup.Application.Id)).Count);
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Fact]
    public async Task ManyParallelUploads_NeverExceed25MbInTotal()
    {
        var setup = await _attachments.NewSetupAsync();
        var fourMb = AttachmentTestFiles.PdfOfSize(4 * 1024 * 1024);

        // 8 x 4 MB: mieści się 6 (24 MB), dwa kolejne przekroczyłyby 25 MB
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            using var client = _api.CreateClient(setup.Candidate);
            return await client.UploadAttachmentAsync(setup.Application.Id, fourMb, $"part{i}.pdf");
        }));

        Assert.Equal(6, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(AttachmentFiles.TotalTooLargeMessage, await rejected.ReadMessageAsync());
        }

        var records = await _attachments.RecordsAsync(setup.Application.Id);
        Assert.Equal(6L * fourMb.Length, records.Sum(r => r.Size));
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Fact]
    public async Task ParallelUploadsAndDeletes_KeepStorageConsistentWithTheRecords()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            Enumerable.Range(0, 6).Select(i => Pdf(i, $"start{i}.pdf")).ToArray());

        for (var round = 0; round < 3; round++)
        {
            var ids = (await _attachments.RecordsAsync(setup.Application.Id)).Select(r => r.Id).ToArray();
            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
            {
                using var client = _api.CreateClient(setup.Candidate);
                return i % 2 == 0 || ids.Length == 0
                    ? await client.UploadAttachmentsAsync(setup.Application.Id, Pdf(round * 10 + i, $"u{round}-{i}.pdf"))
                    : await client.DeleteAttachmentAsync(setup.Application.Id, ids[i % ids.Length]);
            }));

            // 404 - DELETE, który trafił na załącznik usunięty już przez równoległe żądanie; 400 - limit 10 plików
            Assert.All(responses, r => Assert.True(
                r.StatusCode is HttpStatusCode.Created or HttpStatusCode.NoContent or HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
                $"Unexpected {r.StatusCode}"));
            Assert.True((await _attachments.RecordsAsync(setup.Application.Id)).Count <= AttachmentFiles.MaxAttachments);
            await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
        }
    }

    [Fact]
    public async Task ParallelDeletesOfTheSameAttachment_OnlyOneSucceeds()
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf());

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var client = _api.CreateClient(setup.Candidate);
            return await client.DeleteAttachmentAsync(setup.Application.Id, attachment.Id);
        }));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.NoContent), r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task ParallelUploadsToDifferentApplications_DoNotInterfere()
    {
        var setup = await _attachments.NewSetupAsync();
        var others = new List<(TestUser Candidate, Backend.DTOs.JobApplicationDto Application)> { (setup.Candidate, setup.Application) };
        for (var i = 0; i < 5; i++)
        {
            others.Add(await _attachments.ApplyAnotherAsync(setup));
        }

        var responses = await Task.WhenAll(others.Select(async (o, i) =>
        {
            using var client = _api.CreateClient(o.Candidate);
            return await client.UploadAttachmentsAsync(o.Application.Id, Pdf(i * 10, $"cv{i}.pdf"), new AttachmentUpload(AttachmentTestFiles.Png(i), $"img{i}.png"));
        }));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        foreach (var (_, application) in others)
        {
            Assert.Equal(2, (await _attachments.RecordsAsync(application.Id)).Count);
            await _attachments.AssertStorageMatchesRecordsAsync(application.Id);
        }
    }
}
