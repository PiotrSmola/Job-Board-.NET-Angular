using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// CV przy aplikacji: PUT/DELETE/GET /api/jobapplications/{id}/cv, walidacja pliku, uprawnienia i sprzątanie plików
// (magazyn Local w katalogu tymczasowym fabryki)
[Collection(ApiCollection.Name)]
public class CvUploadTests
{
    private const string Url = "/api/jobapplications";
    private const string ApplicationNotFound = "Application not found";
    private const string CvStatusMessage = "CV cannot be changed in the current application status";

    private readonly ApiFixture _api;

    public CvUploadTests(ApiFixture api) => _api = api;

    private sealed record Setup(TestEmployer Employer, JobOfferDto Job, TestUser Candidate, JobApplicationDto Application);

    private async Task<Setup> NewSetupAsync()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        var candidate = await _api.RegisterUserAsync("cv");
        var application = await _api.ApplyAsync(candidate, job.Id);
        return new Setup(employer, job, candidate, application);
    }

    private async Task<JobApplicationDto> UploadOkAsync(TestUser user, int applicationId, byte[] content, string fileName)
    {
        using var client = _api.CreateClient(user);
        var response = await client.UploadCvAsync(applicationId, content, fileName);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<JobApplicationDto>();
    }

    private Task<string?> StoredKeyAsync(int applicationId) =>
        _api.QueryDbAsync(db => db.JobApplications.Where(a => a.Id == applicationId).Select(a => a.CvFileKey).FirstOrDefaultAsync());

    private string StoragePath(string key) => Path.Combine(_api.Factory.StoragePath, key.Replace('/', Path.DirectorySeparatorChar));

    private string[] StoredFilesOf(int applicationId)
    {
        var directory = Path.Combine(_api.Factory.StoragePath, "cv", applicationId.ToString());
        return Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
    }

    // --- upload ---

    [Fact]
    public async Task Upload_Pdf_ReturnsApplicationWithCvAndStoresFileUnderServerKey()
    {
        var setup = await NewSetupAsync();
        var pdf = CvTestFiles.Pdf();

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadCvAsync(setup.Application.Id, pdf, "../../etc/Moje CV.pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("cvFileKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cv/", json);

        var dto = JsonSerializer.Deserialize<JobApplicationDto>(json, ApiJson.Options)!;
        Assert.True(dto.HasCv);
        Assert.Equal("Moje CV.pdf", dto.CvFileName);
        Assert.Equal(pdf.Length, dto.CvSize);
        Assert.NotNull(dto.CvUploadedAt);
        Assert.NotNull(dto.History);

        var key = await StoredKeyAsync(setup.Application.Id);
        Assert.Matches($"^cv/{setup.Application.Id}/[0-9a-f]{{32}}\\.pdf$", key);
        Assert.Equal(pdf, await File.ReadAllBytesAsync(StoragePath(key!)));
    }

    [Fact]
    public async Task Upload_DocxWithPdfExtension_IsStoredAsDocx()
    {
        var setup = await NewSetupAsync();

        var dto = await UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Docx(), "cv.pdf");

        Assert.Equal("cv.docx", dto.CvFileName);
        var contentType = await _api.QueryDbAsync(db => db.JobApplications
            .Where(a => a.Id == setup.Application.Id).Select(a => a.CvContentType).FirstAsync());
        Assert.Equal(CvTestFiles.DocxContentType, contentType);
    }

    public static TheoryData<string, byte[]> UnsupportedFiles => new()
    {
        { "png.pdf", CvTestFiles.Png() },
        { "archive.docx", CvTestFiles.Zip(("readme.txt", "hello")) },
        { "sheet.docx", CvTestFiles.Zip(("[Content_Types].xml", "<Types/>"), ("xl/workbook.xml", "<workbook/>")) },
        { "broken.docx", [0x50, 0x4B, 0x03, 0x04, 0x01, 0x02, 0x03] },
        { "text.pdf", "just text, not a PDF"u8.ToArray() }
    };

    [Theory]
    [MemberData(nameof(UnsupportedFiles))]
    public async Task Upload_UnsupportedContent_ReturnsBadRequestAndStoresNothing(string fileName, byte[] content)
    {
        var setup = await NewSetupAsync();

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadCvAsync(setup.Application.Id, content, fileName);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CvFiles.UnsupportedTypeMessage, await response.ReadMessageAsync());
        Assert.Null(await StoredKeyAsync(setup.Application.Id));
        Assert.Empty(StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task Upload_EmptyFile_ReturnsBadRequest()
    {
        var setup = await NewSetupAsync();

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadCvAsync(setup.Application.Id, []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CvFiles.EmptyFileMessage, await response.ReadMessageAsync());
    }

    [Theory]
    [InlineData(CvFiles.MaxFileSize + 1)]
    [InlineData(CvFiles.MaxRequestSize + 1)]
    public async Task Upload_TooLargeFile_ReturnsBadRequest(long size)
    {
        var setup = await NewSetupAsync();
        var content = CvTestFiles.Pdf(paddingBytes: (int)size - CvTestFiles.Pdf().Length);

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadCvAsync(setup.Application.Id, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CvFiles.TooLargeMessage, await response.ReadMessageAsync());
        Assert.Empty(StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task Upload_FileOfExactlyMaxSize_IsAccepted()
    {
        var setup = await NewSetupAsync();
        var content = CvTestFiles.Pdf(paddingBytes: (int)CvFiles.MaxFileSize - CvTestFiles.Pdf().Length);

        var dto = await UploadOkAsync(setup.Candidate, setup.Application.Id, content, "big.pdf");

        Assert.Equal(CvFiles.MaxFileSize, dto.CvSize);
    }

    [Fact]
    public async Task Upload_WithoutFileField_ReturnsBadRequest()
    {
        var setup = await NewSetupAsync();

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.PutAsync($"{Url}/{setup.Application.Id}/cv",
            CvTestFiles.Form(CvTestFiles.Pdf(), "cv.pdf", fieldName: "document"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ByOtherCandidateEmployerOrAdmin_ReturnsNotFound()
    {
        var setup = await NewSetupAsync();
        var other = await _api.RegisterUserAsync("other");

        using var otherClient = _api.CreateClient(other);
        using var employerClient = _api.CreateClient(setup.Employer);
        using var adminClient = await _api.AdminClientAsync();

        foreach (var client in new[] { otherClient, employerClient, adminClient })
        {
            var response = await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf());
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(ApplicationNotFound, await response.ReadMessageAsync());
        }

        Assert.Null(await StoredKeyAsync(setup.Application.Id));
    }

    [Fact]
    public async Task Upload_WithoutToken_ReturnsUnauthorized()
    {
        var setup = await NewSetupAsync();

        using var client = _api.CreateClient();
        var response = await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ToMissingApplication_ReturnsNotFound()
    {
        using var client = await _api.UserClientAsync();

        var response = await client.UploadCvAsync(int.MaxValue, CvTestFiles.Pdf());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Hired)]
    public async Task Upload_AfterFinalDecision_ReturnsBadRequest(ApplicationStatus status)
    {
        var setup = await NewSetupAsync();
        await _api.SetApplicationStatusAsync(setup.Application.Id, status);

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CvStatusMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Upload_ToWithdrawnApplication_ReturnsBadRequest()
    {
        var setup = await NewSetupAsync();
        Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(setup.Candidate, setup.Application.Id)).StatusCode);

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CvStatusMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Upload_Replacement_DeletesPreviousFile()
    {
        var setup = await NewSetupAsync();
        await UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "first.pdf");
        var firstKey = await StoredKeyAsync(setup.Application.Id);

        var dto = await UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Docx(), "second.docx");

        Assert.Equal("second.docx", dto.CvFileName);
        var secondKey = await StoredKeyAsync(setup.Application.Id);
        Assert.NotEqual(firstKey, secondKey);
        Assert.False(File.Exists(StoragePath(firstKey!)));
        Assert.True(File.Exists(StoragePath(secondKey!)));
        Assert.Single(StoredFilesOf(setup.Application.Id));
    }

    // --- download ---

    [Fact]
    public async Task Download_ByOwnerEmployerAndAdmin_ReturnsFileWithSafeHeaders()
    {
        var setup = await NewSetupAsync();
        var pdf = CvTestFiles.Pdf();
        await UploadOkAsync(setup.Candidate, setup.Application.Id, pdf, "Życiorys: Jan (2026).pdf");

        using var owner = _api.CreateClient(setup.Candidate);
        using var employer = _api.CreateClient(setup.Employer);
        using var admin = await _api.AdminClientAsync();

        foreach (var client in new[] { owner, employer, admin })
        {
            var response = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(pdf, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment; filename*=UTF-8''%C5%BByciorys_%20Jan%20%282026%29.pdf",
                string.Join(",", response.Content.Headers.GetValues("Content-Disposition")));
            Assert.Equal("Życiorys_ Jan (2026).pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
            Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
    }

    [Fact]
    public async Task UploadAndDownload_FileNameWithLineBreaksAndQuotes_CannotInjectHeaders()
    {
        var setup = await NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        var upload = await client.PutAsync($"{Url}/{setup.Application.Id}/cv", CvTestFiles.RawForm(CvTestFiles.Pdf(),
            "filename=\"fallback.pdf\"; filename*=UTF-8''evil%22%0D%0AX-Injected%3A%201.pdf"));

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal("evil_X-Injected_ 1.pdf", (await upload.ReadJsonAsync<JobApplicationDto>()).CvFileName);

        var download = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.False(download.Headers.Contains("X-Injected"));
        Assert.False(download.Content.Headers.Contains("X-Injected"));
        Assert.Equal("evil_X-Injected_ 1.pdf", download.Content.Headers.ContentDisposition?.FileNameStar);
    }

    [Fact]
    public async Task Upload_DocxZipBomb_ReturnsUnsupportedType()
    {
        var setup = await NewSetupAsync();
        var bomb = CvTestFiles.Zip(
            ("[Content_Types].xml", "<Types/>"),
            ("word/document.xml", new string('0', (int)CvFiles.MaxDocxUncompressedSize + 1024)));

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadCvAsync(setup.Application.Id, bomb, "bomb.docx", CvTestFiles.DocxContentType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CvFiles.UnsupportedTypeMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Download_ByOtherEmployerOrCandidate_ReturnsNotFound()
    {
        var setup = await NewSetupAsync();
        await UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");
        var otherEmployer = await _api.RegisterApprovedEmployerAsync();
        var otherCandidate = await _api.RegisterUserAsync("other");

        using var employerClient = _api.CreateClient(otherEmployer);
        using var candidateClient = _api.CreateClient(otherCandidate);

        foreach (var client in new[] { employerClient, candidateClient })
        {
            var response = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(ApplicationNotFound, await response.ReadMessageAsync());
        }
    }

    [Fact]
    public async Task Download_WithoutCv_ReturnsNotFound()
    {
        var setup = await NewSetupAsync();

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("CV not found", await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Download_WhenFileMissingInStorage_ReturnsNotFound()
    {
        var setup = await NewSetupAsync();
        await UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");
        File.Delete(StoragePath((await StoredKeyAsync(setup.Application.Id))!));

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- delete ---

    [Fact]
    public async Task Delete_ByOwner_RemovesCvAndFile()
    {
        var setup = await NewSetupAsync();
        await UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");
        var key = await StoredKeyAsync(setup.Application.Id);

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.DeleteAsync($"{Url}/{setup.Application.Id}/cv");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(StoragePath(key!)));
        var application = await _api.GetApplicationAsync(client, setup.Application.Id);
        Assert.False(application.HasCv);
        Assert.Null(application.CvFileName);
        Assert.Null(application.CvSize);
        Assert.Null(application.CvUploadedAt);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);
    }

    [Fact]
    public async Task Delete_ByEmployerOrAfterRejection_IsRejected()
    {
        var setup = await NewSetupAsync();
        await UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");

        using var employer = _api.CreateClient(setup.Employer);
        Assert.Equal(HttpStatusCode.NotFound, (await employer.DeleteAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);

        await _api.SetApplicationStatusAsync(setup.Application.Id, ApplicationStatus.Rejected);
        using var owner = _api.CreateClient(setup.Candidate);
        var response = await owner.DeleteAsync($"{Url}/{setup.Application.Id}/cv");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CvStatusMessage, await response.ReadMessageAsync());
        Assert.NotNull(await StoredKeyAsync(setup.Application.Id));
    }

    // --- DTO w listach, reaktywacja, sprzątanie ---

    [Fact]
    public async Task Lists_ShowCvMetadata()
    {
        var setup = await NewSetupAsync();
        await UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");

        using var owner = _api.CreateClient(setup.Candidate);
        var mine = await (await owner.GetAsync($"{Url}/my-applications")).ReadJsonAsync<JobApplicationDto[]>();
        Assert.True(Assert.Single(mine).HasCv);

        using var employer = _api.CreateClient(setup.Employer);
        var forJob = await (await employer.GetAsync($"{Url}/job/{setup.Job.Id}")).ReadJsonAsync<JobApplicationDto[]>();
        Assert.Equal("cv.pdf", Assert.Single(forJob).CvFileName);
    }

    [Fact]
    public async Task Reapply_AfterWithdrawal_KeepsCv()
    {
        var setup = await NewSetupAsync();
        await UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");
        Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(setup.Candidate, setup.Application.Id)).StatusCode);

        var reactivated = await _api.ApplyAsync(setup.Candidate, setup.Job.Id);

        Assert.Equal(setup.Application.Id, reactivated.Id);
        Assert.True(reactivated.HasCv);
        Assert.Equal("cv.pdf", reactivated.CvFileName);
    }

    [Fact]
    public async Task AdminDeletesApplication_RemovesCvFile()
    {
        var setup = await NewSetupAsync();
        await UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");
        var key = await StoredKeyAsync(setup.Application.Id);

        using var admin = await _api.AdminClientAsync();
        var response = await admin.DeleteAsync($"{Url}/{setup.Application.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(StoragePath(key!)));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);
    }

    [Fact]
    public async Task AdminDeletesJob_RemovesCvFilesOfItsApplications()
    {
        var setup = await NewSetupAsync();
        var second = await _api.RegisterUserAsync("cv2");
        var secondApplication = await _api.ApplyAsync(second, setup.Job.Id);
        await UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");
        await UploadOkAsync(second, secondApplication.Id, CvTestFiles.Docx(), "cv.docx");
        var keys = new[] { await StoredKeyAsync(setup.Application.Id), await StoredKeyAsync(secondApplication.Id) };

        using var admin = await _api.AdminClientAsync();
        var response = await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.All(keys, key => Assert.False(File.Exists(StoragePath(key!))));
        Assert.False(await _api.QueryDbAsync(db => db.JobApplications.AnyAsync(a => a.JobOfferId == setup.Job.Id)));
    }
}
