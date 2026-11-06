using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// POST /api/jobapplications/{id}/attachments/from-profile: kopie dokumentów profilu jako załączniki aplikacji (nowe
// pliki w magazynie), te same reguły właściciela, statusu i limitów co przy uploadzie
[Collection(ApiCollection.Name)]
public class AttachFromProfileTests
{
    private readonly ApiFixture _api;
    private readonly AttachmentScenario _attachments;

    public AttachFromProfileTests(ApiFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
    }

    private async Task<List<ProfileDocumentDto>> UploadDocumentsAsync(TestUser user, params AttachmentUpload[] uploads)
    {
        using var client = _api.CreateClient(user);
        return await client.UploadDocumentsOkAsync(uploads);
    }

    [Fact]
    public async Task Attach_CopiesTheDocumentsAsNewFilesOfTheApplication()
    {
        var setup = await _attachments.NewSetupAsync();
        var cv = AttachmentTestFiles.Pdf(50);
        var documents = await UploadDocumentsAsync(setup.Candidate,
            new AttachmentUpload(cv, "cv.pdf", AttachmentKind.Cv),
            new AttachmentUpload(AttachmentTestFiles.Png(), "portfolio.png", AttachmentKind.Portfolio, AttachmentTestFiles.PngContentType));
        using var client = _api.CreateClient(setup.Candidate);

        var response = await client.AttachFromProfileAsync(setup.Application.Id, documents.Select(d => d.Id).ToArray());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var application = await response.ReadJsonAsync<JobApplicationDto>();
        Assert.Equal(["cv.pdf", "portfolio.png"], application.Attachments.Select(a => a.FileName));
        Assert.Equal([AttachmentKind.Cv, AttachmentKind.Portfolio], application.Attachments.Select(a => a.Kind));
        Assert.Equal(cv.Length + AttachmentTestFiles.Png().Length, application.AttachmentsTotalSize);

        // osobne pliki w katalogu aplikacji - usunięcie dokumentu z profilu nie usuwa załącznika
        var records = await _attachments.RecordsAsync(setup.Application.Id);
        Assert.All(records, r => Assert.StartsWith($"attachments/{setup.Application.Id}/", r.Key));
        Assert.Equal("image/png", records.Single(r => r.Name == "portfolio.png").ContentType);
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);

        foreach (var document in documents)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(AccountApi.DocumentUrl(document.Id))).StatusCode);
        }

        var cvAttachment = application.Attachments.Single(a => a.Kind == AttachmentKind.Cv);
        var download = await client.DownloadAttachmentAsync(setup.Application.Id, cvAttachment.Id);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(cv, await download.Content.ReadAsByteArrayAsync());

        // rekruter widzi kopię
        using var employer = _api.CreateClient(setup.Employer);
        Assert.Equal(HttpStatusCode.OK, (await employer.DownloadAttachmentAsync(setup.Application.Id, cvAttachment.Id)).StatusCode);
    }

    [Theory]
    [InlineData("{\"documentIds\":[]}")]
    [InlineData("{\"documentIds\":null}")]
    [InlineData("{}")]
    public async Task Attach_WithoutDocuments_Returns400(string json)
    {
        var setup = await _attachments.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        var response = await client.PostRawJsonAsync(AccountApi.FromProfileUrl(setup.Application.Id), json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No documents selected", await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Attach_DocumentOfAnotherCandidateOrUnknown_Returns404AndStoresNothing()
    {
        var setup = await _attachments.NewSetupAsync();
        var mine = await UploadDocumentsAsync(setup.Candidate, new AttachmentUpload(AttachmentTestFiles.Pdf(), "cv.pdf"));
        var stranger = await _api.RegisterUserAsync("docs");
        var theirs = await UploadDocumentsAsync(stranger, new AttachmentUpload(AttachmentTestFiles.Pdf(), "cudze.pdf"));
        using var client = _api.CreateClient(setup.Candidate);

        foreach (var ids in new[] { new[] { mine[0].Id, theirs[0].Id }, new[] { int.MaxValue } })
        {
            var response = await client.AttachFromProfileAsync(setup.Application.Id, ids);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Document not found", await response.ReadMessageAsync());
        }

        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task Attach_ToAnotherCandidatesApplication_Returns404()
    {
        var setup = await _attachments.NewSetupAsync();
        var stranger = await _api.RegisterUserAsync("docs");
        var documents = await UploadDocumentsAsync(stranger, new AttachmentUpload(AttachmentTestFiles.Pdf(), "cv.pdf"));
        using var client = _api.CreateClient(stranger);

        var response = await client.AttachFromProfileAsync(setup.Application.Id, documents[0].Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(AttachmentScenario.ApplicationNotFound, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Attach_InALockedStatus_Returns400()
    {
        var setup = await _attachments.NewSetupAsync();
        var documents = await UploadDocumentsAsync(setup.Candidate, new AttachmentUpload(AttachmentTestFiles.Pdf(), "cv.pdf"));
        await _attachments.SetStatusAsync(setup, ApplicationStatus.Rejected);
        using var client = _api.CreateClient(setup.Candidate);

        var response = await client.AttachFromProfileAsync(setup.Application.Id, documents[0].Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentScenario.StatusMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Attach_OverTheApplicationLimits_Returns400AndRemovesTheCopies()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id, Enumerable.Range(0, 9)
            .Select(i => new AttachmentUpload(AttachmentTestFiles.Pdf(), $"a{i}.pdf")).ToArray());
        var documents = await UploadDocumentsAsync(setup.Candidate,
            new AttachmentUpload(AttachmentTestFiles.Pdf(), "d1.pdf"), new AttachmentUpload(AttachmentTestFiles.Pdf(), "d2.pdf"));
        using var client = _api.CreateClient(setup.Candidate);

        var response = await client.AttachFromProfileAsync(setup.Application.Id, documents.Select(d => d.Id).ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("An application can have at most 10 attachments", await response.ReadMessageAsync());
        Assert.Equal(9, (await _attachments.RecordsAsync(setup.Application.Id)).Count);
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Fact]
    public async Task Attach_Over25MegabytesTogetherWithExistingAttachments_Returns400()
    {
        var setup = await _attachments.NewSetupAsync();
        var fiveMegabytes = AttachmentTestFiles.PdfOfSize(5 * 1024 * 1024);
        await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id, Enumerable.Range(0, 4)
            .Select(i => new AttachmentUpload(fiveMegabytes, $"a{i}.pdf")).ToArray());
        var documents = await UploadDocumentsAsync(setup.Candidate,
            new AttachmentUpload(fiveMegabytes, "d1.pdf"), new AttachmentUpload(AttachmentTestFiles.Pdf(), "d2.pdf"));
        using var client = _api.CreateClient(setup.Candidate);

        var response = await client.AttachFromProfileAsync(setup.Application.Id, documents.Select(d => d.Id).ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Total size of attachments cannot exceed 25 MB", await response.ReadMessageAsync());
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Fact]
    public async Task Attach_DuplicateIds_CopyTheDocumentOnce()
    {
        var setup = await _attachments.NewSetupAsync();
        var documents = await UploadDocumentsAsync(setup.Candidate, new AttachmentUpload(AttachmentTestFiles.Pdf(), "cv.pdf"));
        using var client = _api.CreateClient(setup.Candidate);

        var response = await client.AttachFromProfileAsync(setup.Application.Id, documents[0].Id, documents[0].Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single((await response.ReadJsonAsync<JobApplicationDto>()).Attachments);
    }

    [Fact]
    public async Task Attach_WhenTheProfileFileIsMissing_Returns404AndLeavesNoCopies()
    {
        var setup = await _attachments.NewSetupAsync();
        var documents = await UploadDocumentsAsync(setup.Candidate,
            new AttachmentUpload(AttachmentTestFiles.Pdf(), "ok.pdf"), new AttachmentUpload(AttachmentTestFiles.Pdf(), "missing.pdf"));
        var missingId = documents.Single(d => d.FileName == "missing.pdf").Id;
        var missingKey = await _api.QueryDbAsync(db => db.ProfileDocuments
            .Where(d => d.Id == missingId).Select(d => d.FileKey).SingleAsync());
        File.Delete(_attachments.StoragePath(missingKey));
        using var client = _api.CreateClient(setup.Candidate);

        var response = await client.AttachFromProfileAsync(setup.Application.Id, documents.Select(d => d.Id).ToArray());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Document not found", await response.ReadMessageAsync());
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }
}
