using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// Sprzątanie plików załączników po usunięciu aplikacji / oferty i po resecie danych demo
[Collection(ApiCollection.Name)]
public class AttachmentCleanupTests
{
    private const string Url = "/api/jobapplications";

    private readonly ApiFixture _api;
    private readonly AttachmentScenario _attachments;

    public AttachmentCleanupTests(ApiFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
    }

    // Oferta z kilkoma aplikacjami: część z załącznikami (PDF, DOCX, PNG), jedna bez, jedna wycofana z załącznikami
    private async Task<(AttachmentSetup Setup, List<JobApplicationDto> WithFiles, JobApplicationDto WithoutFiles)> JobWithApplicationsAsync()
    {
        var setup = await _attachments.NewSetupAsync();
        var withFiles = new List<JobApplicationDto> { setup.Application };
        await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            new AttachmentUpload(AttachmentTestFiles.Pdf(), "first.pdf", AttachmentKind.Cv),
            new AttachmentUpload(AttachmentTestFiles.Png(), "photo.png", AttachmentKind.Portfolio));

        for (var i = 0; i < 3; i++)
        {
            var (candidate, application) = await _attachments.ApplyAnotherAsync(setup);
            await _attachments.UploadOkAsync(candidate, application.Id,
                i % 2 == 0 ? AttachmentTestFiles.Docx() : AttachmentTestFiles.Pdf(paddingBytes: i), $"cv{i}{(i % 2 == 0 ? ".docx" : ".pdf")}");
            withFiles.Add(application);

            if (i == 2)
            {
                // wycofana aplikacja nadal ma pliki
                Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(candidate, application.Id)).StatusCode);
            }
        }

        var (_, withoutFiles) = await _attachments.ApplyAnotherAsync(setup);
        return (setup, withFiles, withoutFiles);
    }

    private async Task<List<string>> KeysOfAsync(IEnumerable<JobApplicationDto> applications)
    {
        var keys = new List<string>();
        foreach (var application in applications)
        {
            keys.AddRange(await _attachments.StoredKeysAsync(application.Id));
        }

        return keys;
    }

    [Fact]
    public async Task AdminDeletesJob_RemovesEveryAttachmentFileOfItsApplications()
    {
        var (setup, withFiles, withoutFiles) = await JobWithApplicationsAsync();
        var keys = await KeysOfAsync(withFiles);

        Assert.Equal(5, keys.Count);
        Assert.All(keys, key => Assert.True(File.Exists(_attachments.StoragePath(key))));

        using var admin = await _api.AdminClientAsync();
        var response = await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.All(keys, key => Assert.False(File.Exists(_attachments.StoragePath(key)), $"{key} should be deleted"));
        Assert.All(withFiles.Append(withoutFiles), a => Assert.Empty(_attachments.StoredFilesOf(a.Id)));
        // puste katalogi aplikacji też znikają
        Assert.All(withFiles, a => Assert.False(Directory.Exists(_attachments.ApplicationDirectory(a.Id))));
        Assert.False(await _api.QueryDbAsync(db => db.JobApplications.AnyAsync(a => a.JobOfferId == setup.Job.Id)));
        var ids = withFiles.Select(a => a.Id).ToArray();
        Assert.False(await _api.QueryDbAsync(db => db.ApplicationAttachments.AnyAsync(a => ids.Contains(a.JobApplicationId))));
    }

    [Fact]
    public async Task AdminDeletesJob_DoesNotTouchAttachmentFilesOfOtherJobs()
    {
        var (setup, _, _) = await JobWithApplicationsAsync();
        var other = await _attachments.NewSetupAsync();
        var otherAttachment = await _attachments.UploadOneAsync(other.Candidate, other.Application.Id, AttachmentTestFiles.Pdf(), "other.pdf");

        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}")).StatusCode);

        Assert.True(File.Exists(_attachments.StoragePath(otherAttachment.Key)));
        using var owner = _api.CreateClient(other.Candidate);
        Assert.Equal(HttpStatusCode.OK, (await owner.DownloadAttachmentAsync(other.Application.Id, otherAttachment.Id)).StatusCode);
    }

    [Fact]
    public async Task AdminDeletesJob_WhenSomeFilesAreAlreadyMissing_StillSucceedsAndRemovesTheRest()
    {
        var (setup, withFiles, _) = await JobWithApplicationsAsync();
        var keys = await KeysOfAsync(withFiles);

        File.Delete(_attachments.StoragePath(keys[0]));

        using var admin = await _api.AdminClientAsync();
        var response = await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.All(keys, key => Assert.False(File.Exists(_attachments.StoragePath(key))));
    }

    [Fact]
    public async Task AdminDeletesJobWithoutAnyAttachment_Succeeds()
    {
        var setup = await _attachments.NewSetupAsync();

        using var admin = await _api.AdminClientAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}")).StatusCode);
    }

    [Fact]
    public async Task EmployerCannotDeleteJobWithApplications_AndTheirFilesStay()
    {
        var (setup, withFiles, _) = await JobWithApplicationsAsync();
        var keys = await KeysOfAsync(withFiles);

        using var employer = _api.CreateClient(setup.Employer);
        var response = await employer.DeleteAsync($"/api/jobs/{setup.Job.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.All(keys, key => Assert.True(File.Exists(_attachments.StoragePath(key))));
    }

    [Fact]
    public async Task EmployerDeletesJobWithoutApplications_Succeeds()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);

        using var client = _api.CreateClient(employer);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/jobs/{job.Id}")).StatusCode);
    }

    [Fact]
    public async Task AdminDeletesApplicationWithManyAttachments_LeavesNoFileAtAll()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "first.pdf");
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Docx(), "second.docx");
        await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            Enumerable.Range(0, 8).Select(i => new AttachmentUpload(AttachmentTestFiles.Jpeg(i), $"img{i}.jpg")).ToArray());
        Assert.Equal(10, _attachments.StoredFilesOf(setup.Application.Id).Length);

        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);

        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
        Assert.False(Directory.Exists(_attachments.ApplicationDirectory(setup.Application.Id)));
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
    }

    [Fact]
    public async Task AdminDeletesApplicationWithoutAttachments_Succeeds()
    {
        var setup = await _attachments.NewSetupAsync();

        using var admin = await _api.AdminClientAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeletedApplication_CannotHaveAttachmentsDownloadedOrChangedAnymore()
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "cv.pdf");
        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);

        using var owner = _api.CreateClient(setup.Candidate);
        using var employer = _api.CreateClient(setup.Employer);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.DownloadAttachmentAsync(setup.Application.Id, attachment.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await employer.DownloadAttachmentAsync(setup.Application.Id, attachment.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAttachmentAsync(setup.Application.Id, attachment.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf())).StatusCode);
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task NonAdminCannotDeleteApplication_AndAttachmentsStay()
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "cv.pdf");

        using var owner = _api.CreateClient(setup.Candidate);
        using var employer = _api.CreateClient(setup.Employer);

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employer.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);
        Assert.True(File.Exists(_attachments.StoragePath(attachment.Key)));
    }

    [Fact]
    public async Task DeletingTheLastAttachment_RemovesTheApplicationDirectoryButKeepsOtherApplications()
    {
        var setup = await _attachments.NewSetupAsync();
        var (other, otherApplication) = await _attachments.ApplyAnotherAsync(setup);
        var mine = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "a.pdf");
        await _attachments.UploadOkAsync(other, otherApplication.Id, AttachmentTestFiles.Pdf(), "b.pdf");

        using var owner = _api.CreateClient(setup.Candidate);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAttachmentAsync(setup.Application.Id, mine.Id)).StatusCode);

        Assert.False(Directory.Exists(_attachments.ApplicationDirectory(setup.Application.Id)));
        Assert.Single(_attachments.StoredFilesOf(otherApplication.Id));
    }
}

// Reset danych demo (Production z AllowSeedRefresh=true): usuwa wszystkie pliki z prefiksu attachments/, także
// osierocone, i nie rusza plików spoza tego prefiksu
public class AttachmentRefreshSeedTests : IClassFixture<ProductionRefreshEnabledFixture>
{
    private readonly ProductionRefreshEnabledFixture _api;
    private readonly AttachmentScenario _attachments;

    public AttachmentRefreshSeedTests(ProductionRefreshEnabledFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
    }

    [Fact]
    public async Task RefreshSeed_RemovesAllAttachmentFilesIncludingOrphansButNotOtherFiles()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "one.pdf");
        for (var i = 0; i < 3; i++)
        {
            var (candidate, application) = await _attachments.ApplyAnotherAsync(setup);
            await _attachments.UploadManyOkAsync(candidate, application.Id,
                new AttachmentUpload(i == 1 ? AttachmentTestFiles.Docx() : AttachmentTestFiles.Pdf(), $"cv{i}.pdf"),
                new AttachmentUpload(AttachmentTestFiles.Png(i), $"img{i}.png"));
        }

        // plik osierocony (bez aplikacji) i plik innego przeznaczenia
        var orphanDirectory = Path.Combine(_api.Factory.StoragePath, "attachments", "999999");
        Directory.CreateDirectory(orphanDirectory);
        await File.WriteAllBytesAsync(Path.Combine(orphanDirectory, "orphan.pdf"), AttachmentTestFiles.Pdf());
        var otherDirectory = Path.Combine(_api.Factory.StoragePath, "avatars");
        Directory.CreateDirectory(otherDirectory);
        var keep = Path.Combine(otherDirectory, "keep.txt");
        await File.WriteAllTextAsync(keep, "not an attachment");
        Assert.True(_attachments.AllStoredFiles().Length >= 8); // klasa współdzieli magazyn - mogą być też pliki z innych testów

        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/refresh-seed", null)).StatusCode);

        Assert.Empty(_attachments.AllStoredFiles());
        Assert.True(File.Exists(keep));
        Assert.False(await _api.QueryDbAsync(db => db.ApplicationAttachments.AnyAsync()));
    }

    [Fact]
    public async Task RefreshSeed_LeavesStorageUsableForNewUploads()
    {
        var before = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(before.Candidate, before.Application.Id, AttachmentTestFiles.Pdf(), "old.pdf");

        using (var admin = await _api.AdminClientAsync())
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/refresh-seed", null)).StatusCode);
        }

        var after = await _attachments.NewSetupAsync();
        var dto = await _attachments.UploadOkAsync(after.Candidate, after.Application.Id, AttachmentTestFiles.Docx(), "new.docx");

        var attachment = Assert.Single(dto.Attachments);
        Assert.Equal("new.docx", attachment.FileName);
        Assert.Single(_attachments.AllStoredFiles());
        using var employer = _api.CreateClient(after.Employer);
        Assert.Equal(HttpStatusCode.OK, (await employer.DownloadAttachmentAsync(after.Application.Id, attachment.Id)).StatusCode);
    }

    [Fact]
    public async Task RefreshSeed_SeededApplicationsHaveNoAttachments()
    {
        using (var admin = await _api.AdminClientAsync())
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/refresh-seed", null)).StatusCode);
        }

        using var freshAdmin = await _api.AdminClientAsync();
        var response = await freshAdmin.GetAsync("/api/jobapplications/all?pageSize=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var applications = await response.ReadJsonAsync<JobApplicationDto[]>();
        Assert.All(applications, a =>
        {
            Assert.Empty(a.Attachments);
            Assert.Equal(0, a.AttachmentsTotalSize);
        });
    }
}
