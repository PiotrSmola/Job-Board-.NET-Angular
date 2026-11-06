using System.Net;
using Backend.DTOs;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// Sprzątanie plików CV po usunięciu aplikacji / oferty i po resecie danych demo
[Collection(ApiCollection.Name)]
public class CvCleanupTests
{
    private const string Url = "/api/jobapplications";

    private readonly ApiFixture _api;
    private readonly CvScenario _cv;

    public CvCleanupTests(ApiFixture api)
    {
        _api = api;
        _cv = new CvScenario(api);
    }

    // Oferta z kilkoma aplikacjami: część z CV (PDF i DOCX), jedna bez CV, jedna wycofana z CV
    private async Task<(CvSetup Setup, List<JobApplicationDto> WithCv, JobApplicationDto WithoutCv)> JobWithApplicationsAsync()
    {
        var setup = await _cv.NewSetupAsync();
        var withCv = new List<JobApplicationDto> { setup.Application };
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "first.pdf");

        for (var i = 0; i < 3; i++)
        {
            var (candidate, application) = await _cv.ApplyAnotherAsync(setup);
            await _cv.UploadOkAsync(candidate, application.Id, i % 2 == 0 ? CvTestFiles.Docx() : CvTestFiles.Pdf(paddingBytes: i), $"cv{i}{(i % 2 == 0 ? ".docx" : ".pdf")}");
            withCv.Add(application);

            if (i == 2)
            {
                // wycofana aplikacja nadal ma plik CV
                Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(candidate, application.Id)).StatusCode);
            }
        }

        var (_, withoutCv) = await _cv.ApplyAnotherAsync(setup);
        return (setup, withCv, withoutCv);
    }

    [Fact]
    public async Task AdminDeletesJob_RemovesEveryCvFileOfItsApplications()
    {
        var (setup, withCv, withoutCv) = await JobWithApplicationsAsync();
        var keys = new List<string>();
        foreach (var application in withCv)
        {
            keys.Add((await _cv.StoredKeyAsync(application.Id))!);
        }

        Assert.All(keys, key => Assert.True(File.Exists(_cv.StoragePath(key))));

        using var admin = await _api.AdminClientAsync();
        var response = await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.All(keys, key => Assert.False(File.Exists(_cv.StoragePath(key)), $"{key} should be deleted"));
        Assert.All(withCv.Append(withoutCv), a => Assert.Empty(_cv.StoredFilesOf(a.Id)));
        // puste katalogi aplikacji też znikają
        Assert.All(withCv, a => Assert.False(Directory.Exists(Path.Combine(_api.Factory.StoragePath, "cv", a.Id.ToString()))));
        Assert.False(await _api.QueryDbAsync(db => db.JobApplications.AnyAsync(a => a.JobOfferId == setup.Job.Id)));
    }

    [Fact]
    public async Task AdminDeletesJob_DoesNotTouchCvFilesOfOtherJobs()
    {
        var (setup, _, _) = await JobWithApplicationsAsync();
        var other = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(other.Candidate, other.Application.Id, CvTestFiles.Pdf(), "other.pdf");
        var otherKey = (await _cv.StoredKeyAsync(other.Application.Id))!;

        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}")).StatusCode);

        Assert.True(File.Exists(_cv.StoragePath(otherKey)));
        using var owner = _api.CreateClient(other.Candidate);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"{Url}/{other.Application.Id}/cv")).StatusCode);
    }

    [Fact]
    public async Task AdminDeletesJob_WhenSomeFilesAreAlreadyMissing_StillSucceedsAndRemovesTheRest()
    {
        var (setup, withCv, _) = await JobWithApplicationsAsync();
        var keys = new List<string>();
        foreach (var application in withCv)
        {
            keys.Add((await _cv.StoredKeyAsync(application.Id))!);
        }

        File.Delete(_cv.StoragePath(keys[0]));

        using var admin = await _api.AdminClientAsync();
        var response = await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.All(keys, key => Assert.False(File.Exists(_cv.StoragePath(key))));
    }

    [Fact]
    public async Task AdminDeletesJobWithoutAnyCv_Succeeds()
    {
        var setup = await _cv.NewSetupAsync();

        using var admin = await _api.AdminClientAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}")).StatusCode);
    }

    [Fact]
    public async Task EmployerCannotDeleteJobWithApplications_AndTheirCvFilesStay()
    {
        var (setup, withCv, _) = await JobWithApplicationsAsync();
        var keys = new List<string>();
        foreach (var application in withCv)
        {
            keys.Add((await _cv.StoredKeyAsync(application.Id))!);
        }

        using var employer = _api.CreateClient(setup.Employer);
        var response = await employer.DeleteAsync($"/api/jobs/{setup.Job.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.All(keys, key => Assert.True(File.Exists(_cv.StoragePath(key))));
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
    public async Task AdminDeletesApplicationAfterReplacingCv_LeavesNoFileAtAll()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "first.pdf");
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Docx(), "second.docx");
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(paddingBytes: 9), "third.pdf");

        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);

        Assert.Empty(_cv.StoredFilesOf(setup.Application.Id));
        Assert.False(Directory.Exists(Path.Combine(_api.Factory.StoragePath, "cv", setup.Application.Id.ToString())));
    }

    [Fact]
    public async Task AdminDeletesApplicationWithoutCv_Succeeds()
    {
        var setup = await _cv.NewSetupAsync();

        using var admin = await _api.AdminClientAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeletedApplication_CannotHaveCvDownloadedOrChangedAnymore()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");
        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);

        using var owner = _api.CreateClient(setup.Candidate);
        using var employer = _api.CreateClient(setup.Employer);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await employer.GetAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf())).StatusCode);
        Assert.Empty(_cv.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task NonAdminCannotDeleteApplication_AndCvStays()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");
        var key = (await _cv.StoredKeyAsync(setup.Application.Id))!;

        using var owner = _api.CreateClient(setup.Candidate);
        using var employer = _api.CreateClient(setup.Employer);

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employer.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);
        Assert.True(File.Exists(_cv.StoragePath(key)));
    }

    [Fact]
    public async Task DeletingCv_RemovesTheApplicationDirectoryButKeepsOtherApplications()
    {
        var setup = await _cv.NewSetupAsync();
        var (other, otherApplication) = await _cv.ApplyAnotherAsync(setup);
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "a.pdf");
        await _cv.UploadOkAsync(other, otherApplication.Id, CvTestFiles.Pdf(), "b.pdf");

        using var owner = _api.CreateClient(setup.Candidate);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);

        Assert.False(Directory.Exists(Path.Combine(_api.Factory.StoragePath, "cv", setup.Application.Id.ToString())));
        Assert.Single(_cv.StoredFilesOf(otherApplication.Id));
    }
}

// Reset danych demo (Production z AllowSeedRefresh=true): usuwa wszystkie pliki z prefiksu cv/, także osierocone,
// i nie rusza plików spoza tego prefiksu
public class CvRefreshSeedTests : IClassFixture<ProductionRefreshEnabledFixture>
{
    private readonly ProductionRefreshEnabledFixture _api;
    private readonly CvScenario _cv;

    public CvRefreshSeedTests(ProductionRefreshEnabledFixture api)
    {
        _api = api;
        _cv = new CvScenario(api);
    }

    [Fact]
    public async Task RefreshSeed_RemovesAllCvFilesIncludingOrphansButNotOtherFiles()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "one.pdf");
        for (var i = 0; i < 3; i++)
        {
            var (candidate, application) = await _cv.ApplyAnotherAsync(setup);
            await _cv.UploadOkAsync(candidate, application.Id, i == 1 ? CvTestFiles.Docx() : CvTestFiles.Pdf(), $"cv{i}.pdf");
        }

        // plik osierocony (bez aplikacji) i plik innego przeznaczenia
        var orphanDirectory = Path.Combine(_api.Factory.StoragePath, "cv", "999999");
        Directory.CreateDirectory(orphanDirectory);
        await File.WriteAllBytesAsync(Path.Combine(orphanDirectory, "orphan.pdf"), CvTestFiles.Pdf());
        var otherDirectory = Path.Combine(_api.Factory.StoragePath, "avatars");
        Directory.CreateDirectory(otherDirectory);
        var keep = Path.Combine(otherDirectory, "keep.txt");
        await File.WriteAllTextAsync(keep, "not a CV");
        Assert.True(_cv.AllStoredFiles().Length >= 5); // klasa współdzieli magazyn - mogą być też pliki z innych testów

        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/refresh-seed", null)).StatusCode);

        Assert.Empty(_cv.AllStoredFiles());
        Assert.True(File.Exists(keep));
        Assert.False(await _api.QueryDbAsync(db => db.JobApplications.AnyAsync(a => a.CvFileKey != null)));
    }

    [Fact]
    public async Task RefreshSeed_LeavesStorageUsableForNewUploads()
    {
        var before = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(before.Candidate, before.Application.Id, CvTestFiles.Pdf(), "old.pdf");

        using (var admin = await _api.AdminClientAsync())
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/refresh-seed", null)).StatusCode);
        }

        var after = await _cv.NewSetupAsync();
        var dto = await _cv.UploadOkAsync(after.Candidate, after.Application.Id, CvTestFiles.Docx(), "new.docx");

        Assert.Equal("new.docx", dto.CvFileName);
        Assert.Single(_cv.AllStoredFiles());
        using var employer = _api.CreateClient(after.Employer);
        Assert.Equal(HttpStatusCode.OK, (await employer.GetAsync($"/api/jobapplications/{after.Application.Id}/cv")).StatusCode);
    }

    [Fact]
    public async Task RefreshSeed_SeededApplicationsHaveNoCv()
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
            Assert.False(a.HasCv);
            Assert.Null(a.CvFileName);
        });
    }
}
