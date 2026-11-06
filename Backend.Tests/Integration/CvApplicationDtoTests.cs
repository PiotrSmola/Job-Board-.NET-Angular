using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Metadane CV (hasCv, cvFileName, cvSize, cvUploadedAt) we wszystkich odpowiedziach z aplikacją i brak
// danych magazynu (klucz pliku, content type) w surowym JSON-ie
[Collection(ApiCollection.Name)]
public class CvApplicationDtoTests
{
    private const string Url = "/api/jobapplications";

    private readonly ApiFixture _api;
    private readonly CvScenario _cv;

    public CvApplicationDtoTests(ApiFixture api)
    {
        _api = api;
        _cv = new CvScenario(api);
    }

    // Wszystkie sposoby pobrania aplikacji; zwracają surowy JSON elementu dla danej aplikacji
    private async Task<Dictionary<string, Func<Task<(string Name, JsonElement Element)>>>> ReadersAsync(CvSetup setup)
    {
        var id = setup.Application.Id;
        var owner = _api.CreateClient(setup.Candidate);
        var employer = _api.CreateClient(setup.Employer);
        var admin = await _api.AdminClientAsync();

        async Task<(string, JsonElement)> Single(string name, HttpClient client, string path)
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (name, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
        }

        async Task<(string, JsonElement)> FromList(string name, HttpClient client, string path)
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var list = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            var item = list.EnumerateArray().Single(e => e.GetProperty("id").GetInt32() == id);
            return (name, item.Clone());
        }

        return new()
        {
            ["my-applications"] = () => FromList("my-applications", owner, $"{Url}/my-applications"),
            ["GET {id} (owner)"] = () => Single("GET {id} (owner)", owner, $"{Url}/{id}"),
            ["GET {id} (employer)"] = () => Single("GET {id} (employer)", employer, $"{Url}/{id}"),
            ["GET {id} (admin)"] = () => Single("GET {id} (admin)", admin, $"{Url}/{id}"),
            ["job/{id} (employer)"] = () => FromList("job/{id} (employer)", employer, $"{Url}/job/{setup.Job.Id}"),
            ["job/{id} (admin)"] = () => FromList("job/{id} (admin)", admin, $"{Url}/job/{setup.Job.Id}"),
            ["all (employer)"] = () => FromList("all (employer)", employer, $"{Url}/all?pageSize=50&jobOfferId={setup.Job.Id}"),
            ["all (admin)"] = () => FromList("all (admin)", admin, $"{Url}/all?pageSize=50&jobOfferId={setup.Job.Id}")
        };
    }

    private static void AssertNoStorageDetails(string name, JsonElement element)
    {
        var raw = element.GetRawText();
        foreach (var forbidden in new[] { "cvFileKey", "cvContentType", "fileKey", "contentType", "storage" })
        {
            Assert.False(raw.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"{name}: JSON must not contain '{forbidden}'");
        }

        Assert.DoesNotContain("cv/", raw);
        var cvProperties = element.EnumerateObject().Select(p => p.Name).Where(n => n.StartsWith("cv", StringComparison.OrdinalIgnoreCase)).Order().ToArray();
        Assert.Equal(new[] { "cvFileName", "cvSize", "cvUploadedAt" }, cvProperties);
    }

    [Fact]
    public async Task EveryApplicationResponse_WithCv_ReturnsMetadataAndNoStorageKey()
    {
        var setup = await _cv.NewSetupAsync();
        var pdf = CvTestFiles.Pdf(paddingBytes: 321);
        var before = DateTime.UtcNow.AddSeconds(-5);
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, pdf, "Życiorys Łukasza.pdf");
        var key = (await _cv.StoredKeyAsync(setup.Application.Id))!;

        foreach (var (name, reader) in await ReadersAsync(setup))
        {
            var (_, element) = await reader();

            Assert.True(element.GetProperty("hasCv").GetBoolean(), name);
            Assert.Equal("Życiorys Łukasza.pdf", element.GetProperty("cvFileName").GetString());
            Assert.Equal(pdf.Length, element.GetProperty("cvSize").GetInt64());
            var uploadedAt = element.GetProperty("cvUploadedAt").GetDateTime();
            Assert.InRange(uploadedAt, before, DateTime.UtcNow.AddSeconds(5));
            AssertNoStorageDetails(name, element);
            Assert.DoesNotContain(Path.GetFileName(key), element.GetRawText());
        }
    }

    [Fact]
    public async Task EveryApplicationResponse_WithoutCv_ReturnsHasCvFalseAndNoMetadata()
    {
        var setup = await _cv.NewSetupAsync();

        foreach (var (name, reader) in await ReadersAsync(setup))
        {
            var (_, element) = await reader();

            Assert.False(element.GetProperty("hasCv").GetBoolean(), name);
            foreach (var property in new[] { "cvFileName", "cvSize", "cvUploadedAt" })
            {
                Assert.True(element.GetProperty(property).ValueKind == JsonValueKind.Null, $"{name}: {property} should be null");
            }

            AssertNoStorageDetails(name, element);
        }
    }

    [Fact]
    public async Task EveryApplicationResponse_AfterCvDeletion_ReturnsNoMetadata()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf());
        using var owner = _api.CreateClient(setup.Candidate);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);

        foreach (var (name, reader) in await ReadersAsync(setup))
        {
            var (_, element) = await reader();
            Assert.False(element.GetProperty("hasCv").GetBoolean(), name);
            Assert.Equal(JsonValueKind.Null, element.GetProperty("cvFileName").ValueKind);
            Assert.Equal(JsonValueKind.Null, element.GetProperty("cvSize").ValueKind);
            Assert.Equal(JsonValueKind.Null, element.GetProperty("cvUploadedAt").ValueKind);
        }
    }

    [Fact]
    public async Task ReplacingCv_UpdatesMetadataInEveryResponse()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "old.pdf");
        var docx = CvTestFiles.Docx();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, docx, "new.docx");

        foreach (var (name, reader) in await ReadersAsync(setup))
        {
            var (_, element) = await reader();
            Assert.Equal("new.docx", element.GetProperty("cvFileName").GetString());
            Assert.Equal(docx.Length, element.GetProperty("cvSize").GetInt64());
            AssertNoStorageDetails(name, element);
        }
    }

    [Fact]
    public async Task ResponsesOfOtherEndpoints_CarryTheCvMetadataToo()
    {
        var setup = await _cv.NewSetupAsync();
        var uploaded = await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");
        using var admin = await _api.AdminClientAsync();

        // zmiana statusu przez admina
        var statusResponse = await ApiFixture.ChangeApplicationStatusAsync(admin, setup.Application.Id, ApplicationStatus.InReview);
        var statusRaw = await statusResponse.Content.ReadAsStringAsync();
        var statusDto = JsonSerializer.Deserialize<JobApplicationDto>(statusRaw, ApiJson.Options)!;
        Assert.True(statusDto.HasCv);
        Assert.Equal("cv.pdf", statusDto.CvFileName);
        Assert.Equal(uploaded.CvSize, statusDto.CvSize);
        Assert.Equal(uploaded.CvUploadedAt, statusDto.CvUploadedAt);
        Assert.DoesNotContain("cvFileKey", statusRaw, StringComparison.OrdinalIgnoreCase);

        // wycofanie przez kandydata
        var withdrawResponse = await _api.WithdrawAsync(setup.Candidate, setup.Application.Id);
        var withdrawRaw = await withdrawResponse.Content.ReadAsStringAsync();
        var withdrawn = JsonSerializer.Deserialize<JobApplicationDto>(withdrawRaw, ApiJson.Options)!;
        Assert.True(withdrawn.HasCv);
        Assert.Equal("cv.pdf", withdrawn.CvFileName);
        Assert.DoesNotContain("cvFileKey", withdrawRaw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NewApplication_CreateResponseHasNoCv()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        var candidate = await _api.RegisterUserAsync("fresh");

        using var client = _api.CreateClient(candidate);
        var response = await client.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = job.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var element = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(element.GetProperty("hasCv").GetBoolean());
        Assert.Equal(JsonValueKind.Null, element.GetProperty("cvFileName").ValueKind);
        AssertNoStorageDetails("POST", element);
    }

    [Fact]
    public async Task ManagementListFilteredByJob_ShowsCvOnlyOnApplicationsThatHaveIt()
    {
        var setup = await _cv.NewSetupAsync();
        var (second, secondApplication) = await _cv.ApplyAnotherAsync(setup);
        var (_, thirdApplication) = await _cv.ApplyAnotherAsync(setup);
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "first.pdf");
        await _cv.UploadOkAsync(second, secondApplication.Id, CvTestFiles.Docx(), "second.docx");

        using var employer = _api.CreateClient(setup.Employer);
        var response = await employer.GetAsync($"{Url}/all?pageSize=50&jobOfferId={setup.Job.Id}");
        var applications = (await response.ReadJsonAsync<JobApplicationDto[]>()).ToDictionary(a => a.Id);

        Assert.Equal(3, applications.Count);
        Assert.Equal("first.pdf", applications[setup.Application.Id].CvFileName);
        Assert.Equal("second.docx", applications[secondApplication.Id].CvFileName);
        Assert.False(applications[thirdApplication.Id].HasCv);
        Assert.Null(applications[thirdApplication.Id].CvFileName);
    }

    [Fact]
    public async Task Withdrawing_KeepsCvMetadataAndFile()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf");
        var before = await _cv.CvRecordAsync(setup.Application.Id);

        Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(setup.Candidate, setup.Application.Id)).StatusCode);

        Assert.Equal(before, await _cv.CvRecordAsync(setup.Application.Id));
        Assert.True(File.Exists(_cv.StoragePath(before.Key!)));
        // wycofana aplikacja nadal pozwala właścicielowi i rekruterowi pobrać CV
        using var owner = _api.CreateClient(setup.Candidate);
        using var employer = _api.CreateClient(setup.Employer);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await employer.GetAsync($"{Url}/{setup.Application.Id}/cv")).StatusCode);
    }

    [Fact]
    public async Task Reactivation_KeepsTheSameFileAndAllowsChangingItAgain()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(paddingBytes: 5), "before.pdf");
        var before = await _cv.CvRecordAsync(setup.Application.Id);
        Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(setup.Candidate, setup.Application.Id)).StatusCode);

        var reactivated = await _api.ApplyAsync(setup.Candidate, setup.Job.Id, note: "again");

        Assert.Equal(setup.Application.Id, reactivated.Id);
        Assert.Equal(ApplicationStatus.Submitted, reactivated.Status);
        Assert.True(reactivated.HasCv);
        Assert.Equal("before.pdf", reactivated.CvFileName);
        Assert.Equal(before.Size, reactivated.CvSize);
        Assert.Equal(before, await _cv.CvRecordAsync(setup.Application.Id));
        Assert.True(File.Exists(_cv.StoragePath(before.Key!)));
        Assert.Single(_cv.StoredFilesOf(setup.Application.Id));

        // po reaktywacji status znów pozwala wymienić plik
        var replaced = await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Docx(), "after.docx");
        Assert.Equal("after.docx", replaced.CvFileName);
        Assert.Single(_cv.StoredFilesOf(setup.Application.Id));
        Assert.False(File.Exists(_cv.StoragePath(before.Key!)));
    }
}
