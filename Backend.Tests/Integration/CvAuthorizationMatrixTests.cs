using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Macierz uprawnień do CV: PUT / DELETE / GET {id}/cv x aktor (właściciel, inny kandydat, pracodawca firmy oferty,
// pracodawca innej firmy, admin, anonim) x status aplikacji
[Collection(ApiCollection.Name)]
public class CvAuthorizationMatrixTests
{
    private const string Url = "/api/jobapplications";

    private readonly ApiFixture _api;
    private readonly CvScenario _cv;

    public CvAuthorizationMatrixTests(ApiFixture api)
    {
        _api = api;
        _cv = new CvScenario(api);
    }

    public static TheoryData<ApplicationStatus> AllStatuses => new()
    {
        ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.Interview, ApplicationStatus.Offered,
        ApplicationStatus.Hired, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn
    };

    private sealed class Actors : IDisposable
    {
        public required HttpClient Owner { get; init; }
        public required HttpClient OtherCandidate { get; init; }
        public required HttpClient CompanyEmployer { get; init; }
        public required HttpClient OtherEmployer { get; init; }
        public required HttpClient Admin { get; init; }
        public required HttpClient Anonymous { get; init; }

        public IEnumerable<(string Name, HttpClient Client)> Outsiders =>
        [
            ("other candidate", OtherCandidate), ("other company employer", OtherEmployer)
        ];

        public void Dispose()
        {
            Owner.Dispose();
            OtherCandidate.Dispose();
            CompanyEmployer.Dispose();
            OtherEmployer.Dispose();
            Admin.Dispose();
            Anonymous.Dispose();
        }
    }

    private async Task<Actors> NewActorsAsync(CvSetup setup)
    {
        var otherCandidate = await _api.RegisterUserAsync("outsider");
        var otherEmployer = await _api.RegisterApprovedEmployerAsync();
        return new Actors
        {
            Owner = _api.CreateClient(setup.Candidate),
            OtherCandidate = _api.CreateClient(otherCandidate),
            CompanyEmployer = _api.CreateClient(setup.Employer),
            OtherEmployer = _api.CreateClient(otherEmployer),
            Admin = await _api.AdminClientAsync(),
            Anonymous = _api.CreateClient()
        };
    }

    // --- PUT ---

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Put_OwnerSucceedsOnlyInEditableStatuses(ApplicationStatus status)
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        var response = await actors.Owner.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf(), "owner.pdf");

        if (CvScenario.EditableStatuses.Contains(status))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("owner.pdf", (await response.ReadJsonAsync<JobApplicationDto>()).CvFileName);
            Assert.Single(_cv.StoredFilesOf(setup.Application.Id));
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(CvScenario.CvStatusMessage, await response.ReadMessageAsync());
            Assert.Null(await _cv.StoredKeyAsync(setup.Application.Id));
            Assert.Empty(_cv.StoredFilesOf(setup.Application.Id));
        }
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Put_EveryoneElseIsDeniedInEveryStatus(ApplicationStatus status)
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        var clients = new (string Name, HttpClient Client)[]
        {
            ("other candidate", actors.OtherCandidate), ("company employer", actors.CompanyEmployer),
            ("other company employer", actors.OtherEmployer), ("admin", actors.Admin)
        };
        foreach (var (name, client) in clients)
        {
            var response = await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf());
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{name} got {response.StatusCode}");
            Assert.Equal(CvScenario.ApplicationNotFound, await response.ReadMessageAsync());
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await actors.Anonymous.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf())).StatusCode);
        Assert.Null(await _cv.StoredKeyAsync(setup.Application.Id));
        Assert.Empty(_cv.StoredFilesOf(setup.Application.Id));
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Put_ReplacementInLockedStatusKeepsTheOldFile(ApplicationStatus status)
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "old.pdf");
        var before = await _cv.CvRecordAsync(setup.Application.Id);
        await _cv.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        var response = await actors.Owner.UploadCvAsync(setup.Application.Id, CvTestFiles.Docx(), "new.docx");

        if (CvScenario.EditableStatuses.Contains(status))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("new.docx", (await _cv.CvRecordAsync(setup.Application.Id)).Name);
            Assert.Single(_cv.StoredFilesOf(setup.Application.Id));
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            // bez zmian w rekordzie i bez nowego pliku w magazynie
            Assert.Equal(before, await _cv.CvRecordAsync(setup.Application.Id));
            Assert.True(File.Exists(_cv.StoragePath(before.Key!)));
            Assert.Single(_cv.StoredFilesOf(setup.Application.Id));
        }
    }

    // --- DELETE ---

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Delete_OwnerSucceedsOnlyInEditableStatuses(ApplicationStatus status)
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf());
        var key = await _cv.StoredKeyAsync(setup.Application.Id);
        await _cv.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        var response = await actors.Owner.DeleteAsync($"{Url}/{setup.Application.Id}/cv");

        if (CvScenario.EditableStatuses.Contains(status))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Null(await _cv.StoredKeyAsync(setup.Application.Id));
            Assert.False(File.Exists(_cv.StoragePath(key!)));
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(CvScenario.CvStatusMessage, await response.ReadMessageAsync());
            Assert.Equal(key, await _cv.StoredKeyAsync(setup.Application.Id));
            Assert.True(File.Exists(_cv.StoragePath(key!)));
        }
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Delete_EveryoneElseIsDeniedAndCvStaysIntact(ApplicationStatus status)
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf());
        var before = await _cv.CvRecordAsync(setup.Application.Id);
        await _cv.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        var clients = new (string Name, HttpClient Client)[]
        {
            ("other candidate", actors.OtherCandidate), ("company employer", actors.CompanyEmployer),
            ("other company employer", actors.OtherEmployer), ("admin", actors.Admin)
        };
        foreach (var (name, client) in clients)
        {
            var response = await client.DeleteAsync($"{Url}/{setup.Application.Id}/cv");
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{name} got {response.StatusCode}");
            Assert.Equal(CvScenario.ApplicationNotFound, await response.ReadMessageAsync());
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await actors.Anonymous.DeleteAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);
        Assert.Equal(before, await _cv.CvRecordAsync(setup.Application.Id));
        Assert.True(File.Exists(_cv.StoragePath(before.Key!)));
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Delete_WithoutCv_ReturnsCvNotFoundForOwnerInEveryStatus(ApplicationStatus status)
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        var response = await actors.Owner.DeleteAsync($"{Url}/{setup.Application.Id}/cv");

        // brak CV jest sprawdzany przed statusem (kontrakt: 404 także dla aplikacji odrzuconej bez CV)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("CV not found", await response.ReadMessageAsync());
    }

    // --- GET ---

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Get_OwnerCompanyEmployerAndAdminDownloadInEveryStatus(ApplicationStatus status)
    {
        var setup = await _cv.NewSetupAsync();
        var pdf = CvTestFiles.Pdf(paddingBytes: 100);
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, pdf, "Moje CV.pdf");
        await _cv.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        foreach (var (name, client) in new (string, HttpClient)[]
                 {
                     ("owner", actors.Owner), ("company employer", actors.CompanyEmployer), ("admin", actors.Admin)
                 })
        {
            var response = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{name} got {response.StatusCode}");
            Assert.Equal(pdf, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("Moje CV.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        }
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Get_OutsidersAndAnonymousAreDeniedInEveryStatus(ApplicationStatus status)
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf());
        await _cv.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        foreach (var (name, client) in actors.Outsiders)
        {
            var response = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{name} got {response.StatusCode}");
            Assert.Equal(CvScenario.ApplicationNotFound, await response.ReadMessageAsync());
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await actors.Anonymous.GetAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);
    }

    [Fact]
    public async Task Get_WithoutCv_ReturnsCvNotFoundForAuthorizedAndNotFoundForOutsiders()
    {
        var setup = await _cv.NewSetupAsync();
        using var actors = await NewActorsAsync(setup);

        foreach (var client in new[] { actors.Owner, actors.CompanyEmployer, actors.Admin })
        {
            var response = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("CV not found", await response.ReadMessageAsync());
        }

        // obcy nie dowiaduje się nawet, czy CV istnieje
        foreach (var (_, client) in actors.Outsiders)
        {
            var response = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(CvScenario.ApplicationNotFound, await response.ReadMessageAsync());
        }
    }

    [Fact]
    public async Task Get_EmployerOfCompanyRejectedAfterwards_StillDownloadsCvOfItsApplications()
    {
        // zakres zarządzania pracodawcy wynika z przypisania do firmy, nie ze statusu weryfikacji firmy
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf());
        await _api.RejectCompanyAsync(setup.Employer.CompanyId);

        using var employer = _api.CreateClient(setup.Employer);
        var response = await employer.GetAsync($"{Url}/{setup.Application.Id}/cv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_DifferentApplicationsOfDifferentCandidates_NeverCrossOver()
    {
        var setup = await _cv.NewSetupAsync();
        var (otherCandidate, otherApplication) = await _cv.ApplyAnotherAsync(setup);
        var first = CvTestFiles.Pdf(paddingBytes: 10);
        var second = CvTestFiles.Pdf(paddingBytes: 20);
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, first, "first.pdf");
        await _cv.UploadOkAsync(otherCandidate, otherApplication.Id, second, "second.pdf");

        using var firstClient = _api.CreateClient(setup.Candidate);
        using var secondClient = _api.CreateClient(otherCandidate);

        Assert.Equal(first, await (await firstClient.GetAsync($"{Url}/{setup.Application.Id}/cv")).Content.ReadAsByteArrayAsync());
        Assert.Equal(second, await (await secondClient.GetAsync($"{Url}/{otherApplication.Id}/cv")).Content.ReadAsByteArrayAsync());
        // kandydat nie pobierze cudzego CV ani nie nadpisze/usunie go przez podanie cudzego id
        Assert.Equal(HttpStatusCode.NotFound, (await firstClient.GetAsync($"{Url}/{otherApplication.Id}/cv")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await firstClient.DeleteAsync($"{Url}/{otherApplication.Id}/cv")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await firstClient.UploadCvAsync(otherApplication.Id, first)).StatusCode);
        Assert.Equal("second.pdf", (await _cv.CvRecordAsync(otherApplication.Id)).Name);
    }
}
