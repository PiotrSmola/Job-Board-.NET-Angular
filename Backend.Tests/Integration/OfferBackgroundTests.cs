using System.Net;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Models;
using Backend.Tests.Infrastructure;
using static Backend.Tests.Infrastructure.CompanyMediaTestFiles;

namespace Backend.Tests.Integration;

// Tło nagłówka ofert firmy: upload / zamiana / usunięcie obrazu, przyciemnienie i punkt zaczepienia, publiczny plik
// z nagłówkami cache, companyBackground w ofertach i offerBackground w profilu firmy, moderacja admina
[Collection(ApiCollection.Name)]
public class OfferBackgroundTests
{
    private readonly ApiFixture _api;

    public OfferBackgroundTests(ApiFixture api) => _api = api;

    private async Task<JobOfferDto> GetJobAsync(int jobId, HttpClient? client = null)
    {
        using var anonymous = _api.CreateClient();
        var response = await (client ?? anonymous).GetAsync($"/api/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<JobOfferDto>();
    }

    [Fact]
    public async Task Upload_SetsBackgroundVisibleInJobsAndCompanyProfile()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateJobAsync(j => j.CompanyId = employer.CompanyId);
        using var client = _api.CreateClient(employer);
        Assert.Null((await GetJobAsync(job.Id)).CompanyBackground);

        var background = await client.UploadBackgroundOkAsync(Jpeg(1920, 640));

        Assert.Equal(CompanyMedia.DefaultBackgroundDim, background.Dim);
        Assert.Equal(OfferBackgroundFocus.Center, background.Focus);
        Assert.Matches($"^/api/companies/{employer.CompanyId}/offer-background/file\\?v=[0-9a-f]{{32}}$", background.Url);
        Assert.Matches($"^companies/{employer.CompanyId}/offer-background/[0-9a-f]{{32}}\\.jpg$",
            await _api.BackgroundKeyAsync(employer.CompanyId));

        var details = await GetJobAsync(job.Id);
        Assert.Equal(background.Url, details.CompanyBackground!.Url);
        Assert.Equal(75, details.CompanyBackground.Dim);

        // lista ofert (projekcja z firmą wczytaną w tym samym zapytaniu)
        using var anonymous = _api.CreateClient();
        var list = await anonymous.GetAsync($"/api/jobs?companyId={employer.CompanyId}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(background.Url, (await list.ReadJsonAsync<List<JobOfferDto>>()).Single().CompanyBackground!.Url);

        var company = await (await anonymous.GetAsync($"/api/companies/{employer.CompanyId}")).ReadJsonAsync<CompanyDto>();
        Assert.Equal(background.Url, company.OfferBackground!.Url);
        Assert.Equal(0, company.PhotosCount);
    }

    [Fact]
    public async Task BackgroundFile_OfApprovedCompany_IsPublicInlineImageCachedForADay()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);
        var content = Png(1600, 500);
        var background = await client.UploadBackgroundOkAsync(content);

        using var anonymous = _api.CreateClient();
        var response = await anonymous.GetAsync(background.Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("inline", response.Content.Headers.ContentDisposition!.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("public, max-age=86400", response.RawCacheControl());
    }

    [Fact]
    public async Task PendingCompany_BackgroundIsHiddenFromPublicAndNotCached()
    {
        var employer = await _api.RegisterEmployerAsync();
        var job = await _api.CreateJobAsync(j => j.CompanyId = employer.CompanyId);
        using var client = _api.CreateClient(employer);
        var background = await client.UploadBackgroundOkAsync(Png());

        using var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(PublicBackgroundFileUrl(employer.CompanyId))).StatusCode);

        // oferta firmy niezatwierdzonej (widoczna dla członka) - bez tła; profil firmy członka - z tłem do podglądu
        Assert.Null((await GetJobAsync(job.Id, client)).CompanyBackground);
        var own = await (await client.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        Assert.Equal(background.Url, own.OfferBackground!.Url);

        var file = await client.GetAsync(background.Url);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("private, no-store", file.RawCacheControl());
    }

    [Fact]
    public async Task Replace_DeletesPreviousFileAndChangesUrl()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);
        var first = await client.UploadBackgroundOkAsync(Png());
        var firstKey = await _api.BackgroundKeyAsync(employer.CompanyId);
        await client.PatchJsonAsync(BackgroundUrl, new UpdateOfferBackgroundDto { Dim = 60 });

        var second = await client.UploadBackgroundOkAsync(Jpeg());

        Assert.NotEqual(first.Url, second.Url);
        // ustawienia zostają po zmianie obrazu
        Assert.Equal(60, second.Dim);
        Assert.False(await _api.FileExistsAsync(firstKey!));
        Assert.True(await _api.FileExistsAsync((await _api.BackgroundKeyAsync(employer.CompanyId))!));
    }

    [Fact]
    public async Task Settings_AreValidatedAndSaved()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateJobAsync(j => j.CompanyId = employer.CompanyId);
        using var client = _api.CreateClient(employer);

        // przed dodaniem obrazu: ustawienia zapisywane, url = null
        var withoutImage = await client.PatchJsonAsync(BackgroundUrl, new UpdateOfferBackgroundDto { Dim = 55, Focus = OfferBackgroundFocus.Bottom });
        Assert.Equal(HttpStatusCode.OK, withoutImage.StatusCode);
        var settings = await withoutImage.ReadJsonAsync<OfferBackgroundDto>();
        Assert.Null(settings.Url);
        Assert.Equal((55, OfferBackgroundFocus.Bottom), (settings.Dim, settings.Focus));

        await client.UploadBackgroundOkAsync(Png());
        var response = await client.PatchJsonAsync(BackgroundUrl, new UpdateOfferBackgroundDto { Dim = 95, Focus = OfferBackgroundFocus.Top });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<OfferBackgroundDto>();
        Assert.Equal((95, OfferBackgroundFocus.Top), (updated.Dim, updated.Focus));
        Assert.NotNull(updated.Url);

        // pominięte pole bez zmian
        var dimOnly = await (await client.PatchJsonAsync(BackgroundUrl, new UpdateOfferBackgroundDto { Dim = 80 }))
            .ReadJsonAsync<OfferBackgroundDto>();
        Assert.Equal((80, OfferBackgroundFocus.Top), (dimOnly.Dim, dimOnly.Focus));

        var background = (await GetJobAsync(job.Id)).CompanyBackground!;
        Assert.Equal((80, OfferBackgroundFocus.Top), (background.Dim, background.Focus));

        foreach (var invalid in new[] { """{"dim":54}""", """{"dim":96}""", """{"dim":-1}""", """{"focus":3}""", """{"focus":-1}""" })
        {
            var rejected = await client.PatchRawJsonAsync(BackgroundUrl, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        Assert.Equal(80, (await GetJobAsync(job.Id)).CompanyBackground!.Dim);
    }

    [Fact]
    public async Task Delete_RemovesImageAndFile()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateJobAsync(j => j.CompanyId = employer.CompanyId);
        using var client = _api.CreateClient(employer);
        await client.UploadBackgroundOkAsync(Png());
        var key = await _api.BackgroundKeyAsync(employer.CompanyId);

        var response = await client.DeleteAsync(BackgroundUrl);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await _api.FileExistsAsync(key!));
        Assert.Null((await GetJobAsync(job.Id)).CompanyBackground);
        using var anonymous = _api.CreateClient();
        var file = await anonymous.GetAsync(PublicBackgroundFileUrl(employer.CompanyId));
        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
        Assert.Equal(CompanyMedia.BackgroundNotFoundMessage, await file.ReadMessageAsync());

        // ponowne usunięcie (bez obrazu) - także 204
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(BackgroundUrl)).StatusCode);
    }

    [Fact]
    public async Task Upload_RejectsInvalidFiles()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);

        var gif = await client.UploadBackgroundAsync(AttachmentTestFiles.Gif());
        var huge = await client.UploadBackgroundAsync(Png(13000, 1000));
        var tooLarge = await client.UploadBackgroundAsync(Png(paddingBytes: (int)ImageFiles.MaxFileSize));
        var wrongField = await client.PutAsync(BackgroundUrl, Form("files", Png()));
        var twoFiles = await client.PutAsync(BackgroundUrl, Form("file", Png(), Png()));

        Assert.Equal(ImageFiles.UnsupportedTypeMessage, await gif.ReadMessageAsync());
        Assert.Equal(ImageFiles.DimensionsMessage, await huge.ReadMessageAsync());
        Assert.Equal(ImageFiles.TooLargeMessage, await tooLarge.ReadMessageAsync());
        Assert.Equal(CompanyMedia.NoBackgroundMessage, await wrongField.ReadMessageAsync());
        Assert.Equal(CompanyMedia.NoBackgroundMessage, await twoFiles.ReadMessageAsync());
        Assert.All(new[] { gif, huge, tooLarge, wrongField, twoFiles }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Null(await _api.BackgroundKeyAsync(employer.CompanyId));
    }

    [Fact]
    public async Task EmployerEndpoints_RequireAnEmployer()
    {
        using var anonymous = _api.CreateClient();
        using var candidate = await _api.UserClientAsync();
        using var admin = await _api.AdminClientAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.UploadBackgroundAsync(Png())).StatusCode);
        foreach (var client in new[] { candidate, admin })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.UploadBackgroundAsync(Png())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PatchJsonAsync(BackgroundUrl, new UpdateOfferBackgroundDto { Dim = 70 })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(BackgroundUrl)).StatusCode);
        }
    }

    [Fact]
    public async Task Admin_DeletesBackgroundOfAnyCompany()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);
        await client.UploadBackgroundOkAsync(Png());
        var key = await _api.BackgroundKeyAsync(employer.CompanyId);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(AdminBackgroundUrl(employer.CompanyId))).StatusCode);
        using var candidate = await _api.UserClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.DeleteAsync(AdminBackgroundUrl(employer.CompanyId))).StatusCode);

        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync(AdminBackgroundUrl(int.MaxValue))).StatusCode);
        var response = await admin.DeleteAsync(AdminBackgroundUrl(employer.CompanyId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await _api.BackgroundKeyAsync(employer.CompanyId));
        Assert.False(await _api.FileExistsAsync(key!));
    }
}
