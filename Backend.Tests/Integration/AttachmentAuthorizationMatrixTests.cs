using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Macierz uprawnień do załączników: POST / DELETE / GET {id}/attachments x aktor (właściciel, inny kandydat, pracodawca
// firmy oferty, pracodawca innej firmy, admin, anonim) x status aplikacji
[Collection(ApiCollection.Name)]
public class AttachmentAuthorizationMatrixTests
{
    private readonly ApiFixture _api;
    private readonly AttachmentScenario _attachments;

    public AttachmentAuthorizationMatrixTests(ApiFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
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

        // wszyscy zalogowani poza właścicielem (upload i usuwanie są tylko dla właściciela)
        public IEnumerable<(string Name, HttpClient Client)> NonOwners =>
        [
            ("other candidate", OtherCandidate), ("company employer", CompanyEmployer),
            ("other company employer", OtherEmployer), ("admin", Admin)
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

    private async Task<Actors> NewActorsAsync(AttachmentSetup setup)
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

    // --- POST ---

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Post_OwnerSucceedsOnlyInUploadStatuses(ApplicationStatus status)
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        var response = await actors.Owner.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf(), "owner.pdf");

        if (AttachmentScenario.UploadStatuses.Contains(status))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("owner.pdf", Assert.Single((await response.ReadJsonAsync<JobApplicationDto>()).Attachments).FileName);
            Assert.Single(_attachments.StoredFilesOf(setup.Application.Id));
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(AttachmentScenario.StatusMessage, await response.ReadMessageAsync());
            Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
            Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
        }
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Post_EveryoneElseIsDeniedInEveryStatus(ApplicationStatus status)
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        foreach (var (name, client) in actors.NonOwners)
        {
            var response = await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf());
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{name} got {response.StatusCode}");
            Assert.Equal(AttachmentScenario.ApplicationNotFound, await response.ReadMessageAsync());
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await actors.Anonymous.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf())).StatusCode);
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Post_InLockedStatusKeepsTheExistingAttachments(ApplicationStatus status)
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "old.pdf");
        var before = await _attachments.RecordsAsync(setup.Application.Id);
        await _attachments.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        var response = await actors.Owner.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Docx(), "new.docx");

        if (AttachmentScenario.UploadStatuses.Contains(status))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(2, (await _attachments.RecordsAsync(setup.Application.Id)).Count);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            // bez zmian w rekordach i bez nowego pliku w magazynie
            Assert.Equal(before, await _attachments.RecordsAsync(setup.Application.Id));
        }

        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    // --- DELETE ---

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Delete_OwnerSucceedsInEveryStatus(ApplicationStatus status)
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf());
        await _attachments.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        var response = await actors.Owner.DeleteAttachmentAsync(setup.Application.Id, attachment.Id);

        // prawo do usunięcia danych - także po decyzji końcowej i wycofaniu
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.False(File.Exists(_attachments.StoragePath(attachment.Key)));
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Delete_EveryoneElseIsDeniedAndAttachmentStaysIntact(ApplicationStatus status)
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf());
        var before = await _attachments.RecordsAsync(setup.Application.Id);
        await _attachments.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        foreach (var (name, client) in actors.NonOwners)
        {
            var response = await client.DeleteAttachmentAsync(setup.Application.Id, attachment.Id);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{name} got {response.StatusCode}");
            Assert.Equal(AttachmentScenario.ApplicationNotFound, await response.ReadMessageAsync());
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await actors.Anonymous.DeleteAttachmentAsync(setup.Application.Id, attachment.Id)).StatusCode);
        Assert.Equal(before, await _attachments.RecordsAsync(setup.Application.Id));
        Assert.True(File.Exists(_attachments.StoragePath(attachment.Key)));
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Delete_MissingAttachment_ReturnsAttachmentNotFoundForOwnerInEveryStatus(ApplicationStatus status)
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        var response = await actors.Owner.DeleteAttachmentAsync(setup.Application.Id, int.MaxValue);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(AttachmentScenario.AttachmentNotFound, await response.ReadMessageAsync());
    }

    // --- GET ---

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Get_OwnerCompanyEmployerAndAdminDownloadInEveryStatus(ApplicationStatus status)
    {
        var setup = await _attachments.NewSetupAsync();
        var pdf = AttachmentTestFiles.Pdf(paddingBytes: 100);
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, pdf, "Moje CV.pdf");
        await _attachments.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        foreach (var (name, client) in new (string, HttpClient)[]
                 {
                     ("owner", actors.Owner), ("company employer", actors.CompanyEmployer), ("admin", actors.Admin)
                 })
        {
            var response = await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{name} got {response.StatusCode}");
            Assert.Equal(pdf, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("Moje CV.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        }
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Get_OutsidersAndAnonymousAreDeniedInEveryStatus(ApplicationStatus status)
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf());
        await _attachments.SetStatusAsync(setup, status);
        using var actors = await NewActorsAsync(setup);

        foreach (var (name, client) in actors.Outsiders)
        {
            var response = await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{name} got {response.StatusCode}");
            Assert.Equal(AttachmentScenario.ApplicationNotFound, await response.ReadMessageAsync());
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await actors.Anonymous.DownloadAttachmentAsync(setup.Application.Id, attachment.Id)).StatusCode);
    }

    [Fact]
    public async Task Get_MissingAttachment_ReturnsAttachmentNotFoundForAuthorizedAndApplicationNotFoundForOutsiders()
    {
        var setup = await _attachments.NewSetupAsync();
        using var actors = await NewActorsAsync(setup);

        foreach (var client in new[] { actors.Owner, actors.CompanyEmployer, actors.Admin })
        {
            var response = await client.DownloadAttachmentAsync(setup.Application.Id, int.MaxValue);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(AttachmentScenario.AttachmentNotFound, await response.ReadMessageAsync());
        }

        // obcy nie dowiaduje się nawet, czy załącznik istnieje
        foreach (var (_, client) in actors.Outsiders)
        {
            var response = await client.DownloadAttachmentAsync(setup.Application.Id, int.MaxValue);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(AttachmentScenario.ApplicationNotFound, await response.ReadMessageAsync());
        }
    }

    [Fact]
    public async Task Get_EmployerOfCompanyRejectedAfterwards_StillDownloadsAttachmentsOfItsApplications()
    {
        // zakres zarządzania pracodawcy wynika z przypisania do firmy, nie ze statusu weryfikacji firmy
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf());
        await _api.RejectCompanyAsync(setup.Employer.CompanyId);

        using var employer = _api.CreateClient(setup.Employer);
        var response = await employer.DownloadAttachmentAsync(setup.Application.Id, attachment.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DifferentApplicationsOfDifferentCandidates_NeverCrossOver()
    {
        var setup = await _attachments.NewSetupAsync();
        var (otherCandidate, otherApplication) = await _attachments.ApplyAnotherAsync(setup);
        var first = AttachmentTestFiles.Pdf(paddingBytes: 10);
        var second = AttachmentTestFiles.Pdf(paddingBytes: 20);
        var mine = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, first, "first.pdf");
        var theirs = await _attachments.UploadOneAsync(otherCandidate, otherApplication.Id, second, "second.pdf");

        using var firstClient = _api.CreateClient(setup.Candidate);
        using var secondClient = _api.CreateClient(otherCandidate);

        Assert.Equal(first, await (await firstClient.DownloadAttachmentAsync(setup.Application.Id, mine.Id)).Content.ReadAsByteArrayAsync());
        Assert.Equal(second, await (await secondClient.DownloadAttachmentAsync(otherApplication.Id, theirs.Id)).Content.ReadAsByteArrayAsync());
        // kandydat nie pobierze cudzego załącznika ani nie doda/usunie go przez podanie cudzego id
        Assert.Equal(HttpStatusCode.NotFound, (await firstClient.DownloadAttachmentAsync(otherApplication.Id, theirs.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await firstClient.DownloadAttachmentAsync(setup.Application.Id, theirs.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await firstClient.DeleteAttachmentAsync(otherApplication.Id, theirs.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await firstClient.UploadAttachmentAsync(otherApplication.Id, first)).StatusCode);
        Assert.Equal("second.pdf", Assert.Single(await _attachments.RecordsAsync(otherApplication.Id)).Name);
    }
}
