using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Załączniki (attachments, attachmentsTotalSize) we wszystkich odpowiedziach z aplikacją i brak danych magazynu
// (klucz pliku, content type) w surowym JSON-ie
[Collection(ApiCollection.Name)]
public class AttachmentDtoTests
{
    private const string Url = "/api/jobapplications";

    private static readonly string[] AttachmentProperties = ["fileName", "id", "kind", "size", "uploadedAt"];

    private readonly ApiFixture _api;
    private readonly AttachmentScenario _attachments;

    public AttachmentDtoTests(ApiFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
    }

    // Wszystkie sposoby pobrania aplikacji; zwracają surowy JSON elementu dla danej aplikacji
    private async Task<Dictionary<string, Func<Task<JsonElement>>>> ReadersAsync(AttachmentSetup setup)
    {
        var id = setup.Application.Id;
        var owner = _api.CreateClient(setup.Candidate);
        var employer = _api.CreateClient(setup.Employer);
        var admin = await _api.AdminClientAsync();

        async Task<JsonElement> Single(HttpClient client, string path)
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        async Task<JsonElement> FromList(HttpClient client, string path)
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var list = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            return list.EnumerateArray().Single(e => e.GetProperty("id").GetInt32() == id).Clone();
        }

        return new()
        {
            ["my-applications"] = () => FromList(owner, $"{Url}/my-applications"),
            ["GET {id} (owner)"] = () => Single(owner, $"{Url}/{id}"),
            ["GET {id} (employer)"] = () => Single(employer, $"{Url}/{id}"),
            ["GET {id} (admin)"] = () => Single(admin, $"{Url}/{id}"),
            ["all (employer)"] = () => FromList(employer, $"{Url}/all?pageSize=50&jobOfferId={setup.Job.Id}"),
            ["all (admin)"] = () => FromList(admin, $"{Url}/all?pageSize=50&jobOfferId={setup.Job.Id}")
        };
    }

    private static void AssertNoStorageDetails(string name, JsonElement element)
    {
        var raw = element.GetRawText();
        foreach (var forbidden in new[] { "fileKey", "contentType", "storage", "hasCv", "cvFileName" })
        {
            Assert.False(raw.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"{name}: JSON must not contain '{forbidden}'");
        }

        Assert.DoesNotContain("attachments/", raw);
        foreach (var attachment in element.GetProperty("attachments").EnumerateArray())
        {
            Assert.Equal(AttachmentProperties, attachment.EnumerateObject().Select(p => p.Name).Order().ToArray());
        }
    }

    [Fact]
    public async Task EveryApplicationResponse_WithAttachments_ReturnsThemSortedAndNoStorageKey()
    {
        var setup = await _attachments.NewSetupAsync();
        var pdf = AttachmentTestFiles.Pdf(paddingBytes: 321);
        var png = AttachmentTestFiles.Png(paddingBytes: 10);
        var before = DateTime.UtcNow.AddSeconds(-5);
        // najpierw "inny", potem CV - odpowiedź sortuje wg rodzaju, a w rodzaju wg daty
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, png, "zdjęcie.png", AttachmentKind.Other);
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, pdf, "Życiorys Łukasza.pdf", AttachmentKind.Cv);
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(1), "druga wersja.pdf", AttachmentKind.Cv);
        var records = await _attachments.RecordsAsync(setup.Application.Id);

        foreach (var (name, reader) in await ReadersAsync(setup))
        {
            var element = await reader();
            var attachments = element.GetProperty("attachments").EnumerateArray().ToArray();

            Assert.True(attachments.Length == 3, name);
            Assert.Equal(new[] { "Życiorys Łukasza.pdf", "druga wersja.pdf", "zdjęcie.png" },
                attachments.Select(a => a.GetProperty("fileName").GetString()));
            Assert.Equal(new[] { 0, 0, 3 }, attachments.Select(a => a.GetProperty("kind").GetInt32()));
            Assert.Equal(pdf.Length, attachments[0].GetProperty("size").GetInt64());
            Assert.InRange(attachments[0].GetProperty("uploadedAt").GetDateTime(), before, DateTime.UtcNow.AddSeconds(5));
            Assert.Equal(records.Sum(r => r.Size), element.GetProperty("attachmentsTotalSize").GetInt64());
            AssertNoStorageDetails(name, element);
            Assert.All(records, r => Assert.DoesNotContain(Path.GetFileNameWithoutExtension(r.Key), element.GetRawText()));
        }
    }

    [Fact]
    public async Task EveryApplicationResponse_WithoutAttachments_ReturnsEmptyListAndZeroTotal()
    {
        var setup = await _attachments.NewSetupAsync();

        foreach (var (name, reader) in await ReadersAsync(setup))
        {
            var element = await reader();

            Assert.True(element.GetProperty("attachments").GetArrayLength() == 0, name);
            Assert.Equal(0, element.GetProperty("attachmentsTotalSize").GetInt64());
            AssertNoStorageDetails(name, element);
        }
    }

    [Fact]
    public async Task EveryApplicationResponse_AfterDeletingAnAttachment_DropsIt()
    {
        var setup = await _attachments.NewSetupAsync();
        var removed = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "cv.pdf");
        var kept = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Docx(), "list.docx");
        using var owner = _api.CreateClient(setup.Candidate);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAttachmentAsync(setup.Application.Id, removed.Id)).StatusCode);

        foreach (var (name, reader) in await ReadersAsync(setup))
        {
            var element = await reader();
            var attachment = Assert.Single(element.GetProperty("attachments").EnumerateArray());
            Assert.Equal(kept.Id, attachment.GetProperty("id").GetInt32());
            Assert.True(kept.Size == element.GetProperty("attachmentsTotalSize").GetInt64(), name);
        }
    }

    [Fact]
    public async Task ResponsesOfOtherEndpoints_CarryTheAttachmentsToo()
    {
        var setup = await _attachments.NewSetupAsync();
        var uploaded = await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "cv.pdf", AttachmentKind.Cv);
        var expected = Assert.Single(uploaded.Attachments);
        using var admin = await _api.AdminClientAsync();

        // zmiana statusu przez admina
        var statusResponse = await ApiFixture.ChangeApplicationStatusAsync(admin, setup.Application.Id, ApplicationStatus.InReview);
        var statusRaw = await statusResponse.Content.ReadAsStringAsync();
        var statusDto = JsonSerializer.Deserialize<JobApplicationDto>(statusRaw, ApiJson.Options)!;
        var statusAttachment = Assert.Single(statusDto.Attachments);
        Assert.Equal(expected.Id, statusAttachment.Id);
        Assert.Equal(expected.Size, statusAttachment.Size);
        Assert.Equal(expected.UploadedAt, statusAttachment.UploadedAt);
        Assert.Equal(expected.Size, statusDto.AttachmentsTotalSize);
        Assert.DoesNotContain("fileKey", statusRaw, StringComparison.OrdinalIgnoreCase);

        // wycofanie przez kandydata
        var withdrawResponse = await _api.WithdrawAsync(setup.Candidate, setup.Application.Id);
        var withdrawRaw = await withdrawResponse.Content.ReadAsStringAsync();
        var withdrawn = JsonSerializer.Deserialize<JobApplicationDto>(withdrawRaw, ApiJson.Options)!;
        Assert.Equal("cv.pdf", Assert.Single(withdrawn.Attachments).FileName);
        Assert.DoesNotContain("fileKey", withdrawRaw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NewApplication_CreateResponseHasNoAttachments()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        var candidate = await _api.RegisterUserAsync("fresh");

        using var client = _api.CreateClient(candidate);
        var response = await client.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = job.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var element = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, element.GetProperty("attachments").GetArrayLength());
        Assert.Equal(0, element.GetProperty("attachmentsTotalSize").GetInt64());
        AssertNoStorageDetails("POST", element);
    }

    [Fact]
    public async Task ManagementListFilteredByJob_ShowsAttachmentsOfEachApplication()
    {
        var setup = await _attachments.NewSetupAsync();
        var (second, secondApplication) = await _attachments.ApplyAnotherAsync(setup);
        var (_, thirdApplication) = await _attachments.ApplyAnotherAsync(setup);
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "first.pdf");
        await _attachments.UploadManyOkAsync(second, secondApplication.Id,
            new AttachmentUpload(AttachmentTestFiles.Docx(), "second.docx", AttachmentKind.Cv),
            new AttachmentUpload(AttachmentTestFiles.Jpeg(), "photo.jpg", AttachmentKind.Portfolio));

        using var employer = _api.CreateClient(setup.Employer);
        var response = await employer.GetAsync($"{Url}/all?pageSize=50&jobOfferId={setup.Job.Id}");
        var applications = (await response.ReadJsonAsync<JobApplicationDto[]>()).ToDictionary(a => a.Id);

        Assert.Equal(3, applications.Count);
        Assert.Equal("first.pdf", Assert.Single(applications[setup.Application.Id].Attachments).FileName);
        Assert.Equal(new[] { "second.docx", "photo.jpg" }, applications[secondApplication.Id].Attachments.Select(a => a.FileName));
        Assert.Empty(applications[thirdApplication.Id].Attachments);
        Assert.Equal(0, applications[thirdApplication.Id].AttachmentsTotalSize);
    }

    [Fact]
    public async Task ManagementListPaging_IsNotAffectedByTheNumberOfAttachments()
    {
        // stronicowanie liczy aplikacje, nie wiersze z załącznikami (JOIN kolekcji nie może psuć Skip/Take)
        var setup = await _attachments.NewSetupAsync();
        var (second, secondApplication) = await _attachments.ApplyAnotherAsync(setup);
        await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            Enumerable.Range(0, 5).Select(i => new AttachmentUpload(AttachmentTestFiles.Pdf(i), $"a{i}.pdf")).ToArray());
        await _attachments.UploadManyOkAsync(second, secondApplication.Id,
            Enumerable.Range(0, 3).Select(i => new AttachmentUpload(AttachmentTestFiles.Pdf(i), $"b{i}.pdf")).ToArray());

        using var employer = _api.CreateClient(setup.Employer);
        var ids = new List<int>();
        for (var page = 1; page <= 2; page++)
        {
            var response = await employer.GetAsync($"{Url}/all?pageSize=1&pageNumber={page}&jobOfferId={setup.Job.Id}");
            Assert.Equal(2, response.ReadPagination().TotalItems);
            var item = Assert.Single(await response.ReadJsonAsync<JobApplicationDto[]>());
            Assert.Equal(item.Id == setup.Application.Id ? 5 : 3, item.Attachments.Count);
            ids.Add(item.Id);
        }

        Assert.Equal(new[] { setup.Application.Id, secondApplication.Id }.Order(), ids.Order());
    }

    [Fact]
    public async Task Withdrawing_KeepsAttachmentsAndFiles()
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "cv.pdf");
        var before = await _attachments.RecordsAsync(setup.Application.Id);

        Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(setup.Candidate, setup.Application.Id)).StatusCode);

        Assert.Equal(before, await _attachments.RecordsAsync(setup.Application.Id));
        Assert.True(File.Exists(_attachments.StoragePath(attachment.Key)));
        // wycofana aplikacja nadal pozwala właścicielowi i rekruterowi pobrać załącznik
        using var owner = _api.CreateClient(setup.Candidate);
        using var employer = _api.CreateClient(setup.Employer);
        Assert.Equal(HttpStatusCode.OK, (await owner.DownloadAttachmentAsync(setup.Application.Id, attachment.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await employer.DownloadAttachmentAsync(setup.Application.Id, attachment.Id)).StatusCode);
    }

    [Fact]
    public async Task Reactivation_KeepsTheSameFilesAndAllowsAddingAgain()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(paddingBytes: 5), "before.pdf");
        var before = await _attachments.RecordsAsync(setup.Application.Id);
        Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(setup.Candidate, setup.Application.Id)).StatusCode);

        var reactivated = await _api.ApplyAsync(setup.Candidate, setup.Job.Id, note: "again");

        Assert.Equal(setup.Application.Id, reactivated.Id);
        Assert.Equal(ApplicationStatus.Submitted, reactivated.Status);
        Assert.Equal("before.pdf", Assert.Single(reactivated.Attachments).FileName);
        Assert.Equal(before.Single().Size, reactivated.AttachmentsTotalSize);
        Assert.Equal(before, await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Single(_attachments.StoredFilesOf(setup.Application.Id));

        // po reaktywacji status znów pozwala dodawać pliki
        var added = await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Docx(), "after.docx");
        Assert.Equal(new[] { "before.pdf", "after.docx" }, added.Attachments.Select(a => a.FileName));
        Assert.Equal(2, _attachments.StoredFilesOf(setup.Application.Id).Length);
    }
}
