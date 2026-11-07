using System.Net;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Backend.Tests.Infrastructure.CompanyMediaTestFiles;

namespace Backend.Tests.Integration;

// Galeria zdjęć firmy: upload pracodawcy (wszystko albo nic, limit 20), podpisy, kolejność, usuwanie, publiczny
// odczyt z nagłówkami cache, moderacja admina, autoryzacja i sprzątanie plików
[Collection(ApiCollection.Name)]
public class CompanyPhotoTests
{
    private readonly ApiFixture _api;

    public CompanyPhotoTests(ApiFixture api) => _api = api;

    private async Task<List<CompanyPhotoDto>> PublicPhotosAsync(int companyId, HttpClient? client = null)
    {
        using var anonymous = _api.CreateClient();
        var response = await (client ?? anonymous).GetAsync(PublicPhotosUrl(companyId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<List<CompanyPhotoDto>>();
    }

    // pliki firmy w katalogu magazynu Local (także osierocone - bez rekordów)
    private int StoredFileCount(int companyId)
    {
        var directory = Path.Combine(_api.Factory.StoragePath, "companies", companyId.ToString());
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count() : 0;
    }

    // --- upload i publiczny odczyt ---

    [Fact]
    public async Task Upload_StoresPhotosWithDimensionsAndPublicListReturnsThemInOrder()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);

        var photos = await client.UploadPhotosOkAsync(Png(1600, 900), Jpeg(800, 1200));

        Assert.Equal(2, photos.Count);
        Assert.Equal((1600, 900), (photos[0].Width, photos[0].Height));
        Assert.Equal((800, 1200), (photos[1].Width, photos[1].Height));
        Assert.All(photos, p => Assert.Null(p.Caption));

        var publicPhotos = await PublicPhotosAsync(employer.CompanyId);
        Assert.Equal(photos.Select(p => p.Id), publicPhotos.Select(p => p.Id));
        Assert.Equal(photos.Select(p => p.Url), publicPhotos.Select(p => p.Url));

        var keys = await _api.PhotoKeysAsync(employer.CompanyId);
        Assert.Matches($"^companies/{employer.CompanyId}/photos/[0-9a-f]{{32}}\\.png$", keys[0]);
        Assert.Matches($"^companies/{employer.CompanyId}/photos/[0-9a-f]{{32}}\\.jpg$", keys[1]);

        // adres z wersją = GUID pliku: publiczny cache (1 dzień) nigdy nie pokaże pod tym adresem innego obrazu
        for (var i = 0; i < photos.Count; i++)
        {
            Assert.Equal($"/api/companies/{employer.CompanyId}/photos/{photos[i].Id}/file?v={Path.GetFileNameWithoutExtension(keys[i])}",
                photos[i].Url);
        }

        // kolejne zdjęcia trafiają na koniec galerii
        var more = await client.UploadPhotosOkAsync(Png(10, 10));
        Assert.Equal(photos.Select(p => p.Id).Append(more[2].Id), more.Select(p => p.Id));
    }

    [Fact]
    public async Task PhotoFile_OfApprovedCompany_IsPublicInlineImageCachedForADay()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);
        var content = Jpeg(640, 480);
        var photo = (await client.UploadPhotosOkAsync(content)).Single();

        using var anonymous = _api.CreateClient();
        var response = await anonymous.GetAsync(photo.Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("inline", response.Content.Headers.ContentDisposition!.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("public, max-age=86400", response.RawCacheControl());

        // zalogowany członek firmy dostaje ten sam publiczny obraz (bez no-store z SecurityHeadersMiddleware)
        var own = await client.GetAsync(photo.Url);
        Assert.Equal("public, max-age=86400", own.RawCacheControl());
    }

    [Fact]
    public async Task PendingCompany_PhotosAreVisibleOnlyToMembersAndAdminWithoutCaching()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);
        var photo = (await client.UploadPhotosOkAsync(Png())).Single();

        using var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(PublicPhotosUrl(employer.CompanyId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(photo.Url)).StatusCode);

        var other = await _api.RegisterApprovedEmployerAsync();
        using var otherClient = _api.CreateClient(other);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync(photo.Url)).StatusCode);

        var own = await client.GetAsync(photo.Url);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("private, no-store", own.RawCacheControl());

        using var admin = await _api.AdminClientAsync();
        Assert.Single(await PublicPhotosAsync(employer.CompanyId, admin));
        var adminFile = await admin.GetAsync(photo.Url);
        Assert.Equal(HttpStatusCode.OK, adminFile.StatusCode);
        Assert.Equal("private, no-store", adminFile.RawCacheControl());
    }

    [Fact]
    public async Task PhotoFile_OfAnotherCompany_IsNotFound()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);
        var photo = (await client.UploadPhotosOkAsync(Png())).Single();
        var other = await _api.RegisterApprovedEmployerAsync();

        using var anonymous = _api.CreateClient();
        var response = await anonymous.GetAsync(PublicPhotoFileUrl(other.CompanyId, photo.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(CompanyMedia.PhotoNotFoundMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task CompanyDto_ContainsPhotosCount()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);
        await client.UploadPhotosOkAsync(Png(), Png(), Png());

        using var anonymous = _api.CreateClient();
        var company = await (await anonymous.GetAsync($"/api/companies/{employer.CompanyId}")).ReadJsonAsync<CompanyDto>();
        Assert.Equal(3, company.PhotosCount);

        var list = await anonymous.GetAsync($"/api/companies?searchTerm={Uri.EscapeDataString(employer.CompanyName)}");
        Assert.Equal(3, (await list.ReadJsonAsync<List<CompanyDto>>()).Single().PhotosCount);

        var own = await (await client.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        Assert.Equal(3, own.PhotosCount);
    }

    // --- walidacja i limity ---

    public static TheoryData<string, byte[], string> InvalidFiles => new()
    {
        { "gif", AttachmentTestFiles.Gif(), ImageFiles.UnsupportedTypeMessage },
        { "pdf", AttachmentTestFiles.Pdf(), ImageFiles.UnsupportedTypeMessage },
        { "png-without-ihdr", AttachmentTestFiles.Png(), ImageFiles.UnsupportedTypeMessage },
        { "too-large", Png(paddingBytes: (int)ImageFiles.MaxFileSize), ImageFiles.TooLargeMessage },
        { "huge-dimensions", Png(20000, 20000), ImageFiles.DimensionsMessage },
        { "empty", [], ImageFiles.EmptyFileMessage }
    };

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public async Task Upload_WithAnyInvalidFile_RejectsAllFilesAndStoresNothing(string name, byte[] invalid, string message)
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.UploadPhotosAsync(Png(), invalid);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, name);
        Assert.Equal(message, await response.ReadMessageAsync());
        Assert.Empty(await _api.PhotoKeysAsync(employer.CompanyId));
        Assert.Equal(0, StoredFileCount(employer.CompanyId));
    }

    [Fact]
    public async Task Upload_WithoutFilesField_ReturnsBadRequest()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);

        var wrongField = await client.PostAsync(PhotosUrl, Form("file", Png()));
        var notMultipart = await client.PostJsonAsync(PhotosUrl, new { files = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, wrongField.StatusCode);
        Assert.Equal(CompanyMedia.NoPhotosMessage, await wrongField.ReadMessageAsync());
        Assert.Equal(HttpStatusCode.BadRequest, notMultipart.StatusCode);
        Assert.Equal(CompanyMedia.NoPhotosMessage, await notMultipart.ReadMessageAsync());
    }

    [Fact]
    public async Task Upload_AboveTwentyPhotos_IsRejectedAndKeepsExistingPhotos()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);

        // 21 plików w jednym żądaniu
        var tooMany = await client.UploadPhotosAsync(Enumerable.Range(0, 21).Select(_ => Png()).ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal(CompanyMedia.TooManyPhotosMessage, await tooMany.ReadMessageAsync());

        // 19 + 2 przekracza limit łącznie z istniejącymi - nic nie jest dodawane
        await client.UploadPhotosOkAsync(Enumerable.Range(0, 19).Select(_ => Png()).ToArray());
        var overLimit = await client.UploadPhotosAsync(Png(), Png());
        Assert.Equal(HttpStatusCode.BadRequest, overLimit.StatusCode);
        Assert.Equal(CompanyMedia.TooManyPhotosMessage, await overLimit.ReadMessageAsync());
        Assert.Equal(19, (await _api.PhotoKeysAsync(employer.CompanyId)).Count);

        // dokładnie 20, potem pełna galeria odrzucana jeszcze przed odczytem formularza
        Assert.Equal(20, (await client.UploadPhotosOkAsync(Png())).Count);
        var full = await client.UploadPhotosAsync(Png());
        Assert.Equal(HttpStatusCode.BadRequest, full.StatusCode);
        Assert.Equal(CompanyMedia.TooManyPhotosMessage, await full.ReadMessageAsync());
    }

    // równoległe uploady nie przekraczają limitu (blokada wiersza firmy), a odrzucone nie zostawiają plików
    [Fact]
    public async Task ParallelUploads_NeverExceedTheLimit()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);
        await client.UploadPhotosOkAsync(Enumerable.Range(0, 16).Select(_ => Png()).ToArray());

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var parallel = _api.CreateClient(employer);
            return await parallel.UploadPhotosAsync(Png(), Png());
        }));

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
        var keys = await _api.PhotoKeysAsync(employer.CompanyId);
        Assert.Equal(20, keys.Count);
        Assert.Equal(20, StoredFileCount(employer.CompanyId));
    }

    // --- podpis, kolejność, usuwanie ---

    [Fact]
    public async Task Caption_IsNormalizedLimitedAndClearable()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);
        var photo = (await client.UploadPhotosOkAsync(Png())).Single();

        var updated = await client.PatchJsonAsync(PhotoUrl(photo.Id), new UpdateCompanyPhotoDto { Caption = "  Nasze\tbiuro  " });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Nasze biuro", (await updated.ReadJsonAsync<CompanyPhotoDto>()).Caption);
        Assert.Equal("Nasze biuro", (await PublicPhotosAsync(employer.CompanyId)).Single().Caption);

        var tooLong = await client.PatchJsonAsync(PhotoUrl(photo.Id), new UpdateCompanyPhotoDto { Caption = new string('a', 151) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var maxLength = await client.PatchJsonAsync(PhotoUrl(photo.Id), new UpdateCompanyPhotoDto { Caption = new string('a', 150) });
        Assert.Equal(HttpStatusCode.OK, maxLength.StatusCode);

        var cleared = await client.PatchJsonAsync(PhotoUrl(photo.Id), new UpdateCompanyPhotoDto { Caption = "   " });
        Assert.Null((await cleared.ReadJsonAsync<CompanyPhotoDto>()).Caption);
    }

    [Fact]
    public async Task Order_ChangesGalleryOrderAndRejectsIncompleteLists()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);
        var ids = (await client.UploadPhotosOkAsync(Png(), Png(), Png())).Select(p => p.Id).ToList();
        using var otherClient = _api.CreateClient(await _api.RegisterApprovedEmployerAsync());
        var foreign = (await otherClient.UploadPhotosOkAsync(Png())).Single().Id;

        var reversed = Enumerable.Reverse(ids).ToList();
        var response = await client.PutJsonAsync(PhotoOrderUrl, new CompanyPhotoOrderDto { PhotoIds = reversed });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(reversed, (await response.ReadJsonAsync<List<CompanyPhotoDto>>()).Select(p => p.Id));
        Assert.Equal(reversed, (await PublicPhotosAsync(employer.CompanyId)).Select(p => p.Id));

        foreach (var invalid in new List<int>?[]
                 {
                     null,
                     [ids[0], ids[1]],
                     [ids[0], ids[1], ids[1]],
                     [ids[0], ids[1], ids[2], ids[2]],
                     [ids[0], ids[1], foreign],
                     [ids[0], ids[1], ids[2], foreign]
                 })
        {
            var rejected = await client.PutJsonAsync(PhotoOrderUrl, new CompanyPhotoOrderDto { PhotoIds = invalid });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(CompanyMedia.InvalidOrderMessage, await rejected.ReadMessageAsync());
        }

        Assert.Equal(reversed, (await PublicPhotosAsync(employer.CompanyId)).Select(p => p.Id));
    }

    [Fact]
    public async Task Delete_RemovesRecordAndFile()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);
        var photos = await client.UploadPhotosOkAsync(Png(), Png());
        var key = (await _api.PhotoKeysAsync(employer.CompanyId))[0];

        var response = await client.DeleteAsync(PhotoUrl(photos[0].Id));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await _api.FileExistsAsync(key));
        Assert.Equal([photos[1].Id], (await PublicPhotosAsync(employer.CompanyId)).Select(p => p.Id));
        using var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(photos[0].Url)).StatusCode);

        var again = await client.DeleteAsync(PhotoUrl(photos[0].Id));
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(CompanyMedia.PhotoNotFoundMessage, await again.ReadMessageAsync());
    }

    // --- autoryzacja ---

    [Fact]
    public async Task EmployerEndpoints_RequireAnEmployer()
    {
        using var anonymous = _api.CreateClient();
        using var candidate = await _api.UserClientAsync();
        using var admin = await _api.AdminClientAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.UploadPhotosAsync(Png())).StatusCode);
        foreach (var client in new[] { candidate, admin })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.UploadPhotosAsync(Png())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PatchJsonAsync(PhotoUrl(1), new UpdateCompanyPhotoDto { Caption = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PutJsonAsync(PhotoOrderUrl, new CompanyPhotoOrderDto { PhotoIds = [] })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(PhotoUrl(1))).StatusCode);
        }
    }

    [Fact]
    public async Task ForeignEmployer_CannotChangeOrDeleteAnotherCompanysPhoto()
    {
        var owner = await _api.RegisterApprovedEmployerAsync();
        using var ownerClient = _api.CreateClient(owner);
        var photo = (await ownerClient.UploadPhotosOkAsync(Png())).Single();
        var stranger = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(stranger);

        var caption = await client.PatchJsonAsync(PhotoUrl(photo.Id), new UpdateCompanyPhotoDto { Caption = "Hacked" });
        var delete = await client.DeleteAsync(PhotoUrl(photo.Id));
        var order = await client.PutJsonAsync(PhotoOrderUrl, new CompanyPhotoOrderDto { PhotoIds = [photo.Id] });

        Assert.Equal(HttpStatusCode.NotFound, caption.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, order.StatusCode);
        var photos = await PublicPhotosAsync(owner.CompanyId);
        Assert.Null(photos.Single().Caption);
    }

    [Fact]
    public async Task EmployerWithoutCompany_GetsNotFound()
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.QueryDbAsync(db =>
            db.Users.Where(u => u.Id == employer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.CompanyId, (int?)null)));
        using var client = _api.CreateClient(employer);

        var response = await client.UploadPhotosAsync(Png());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(CompanyMessages.NotFound, await response.ReadMessageAsync());
    }

    // --- moderacja admina ---

    [Fact]
    public async Task Admin_DeletesPhotoOfAnyCompany()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);
        var photo = (await client.UploadPhotosOkAsync(Png())).Single();
        var key = (await _api.PhotoKeysAsync(employer.CompanyId)).Single();

        // pracodawca i kandydat nie mają dostępu do moderacji
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(AdminPhotoUrl(employer.CompanyId, photo.Id))).StatusCode);
        using var candidate = await _api.UserClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.DeleteAsync(AdminPhotoUrl(employer.CompanyId, photo.Id))).StatusCode);

        using var admin = await _api.AdminClientAsync();
        var wrongCompany = await admin.DeleteAsync(AdminPhotoUrl(_api.DefaultCompanyId, photo.Id));
        Assert.Equal(HttpStatusCode.NotFound, wrongCompany.StatusCode);
        Assert.Equal(CompanyMedia.PhotoNotFoundMessage, await wrongCompany.ReadMessageAsync());
        var missingCompany = await admin.DeleteAsync(AdminPhotoUrl(int.MaxValue, photo.Id));
        Assert.Equal(CompanyMessages.NotFound, await missingCompany.ReadMessageAsync());

        var response = await admin.DeleteAsync(AdminPhotoUrl(employer.CompanyId, photo.Id));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await PublicPhotosAsync(employer.CompanyId));
        Assert.False(await _api.FileExistsAsync(key));
    }

    // --- sprzątanie plików przy usunięciu firmy ---

    [Fact]
    public async Task DeletingLastMembersAccount_DeletesCompanyMediaFiles()
    {
        // firma oczekująca bez ofert jest usuwana razem z kontem jedynego członka
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);
        await client.UploadPhotosOkAsync(Png(), Jpeg());
        await client.UploadBackgroundOkAsync(Png(1920, 600));
        var keys = await _api.PhotoKeysAsync(employer.CompanyId);
        keys.Add((await _api.BackgroundKeyAsync(employer.CompanyId))!);
        foreach (var key in keys)
        {
            Assert.True(await _api.FileExistsAsync(key), key);
        }

        var response = await client.DeleteAccountAsync(employer.User.Password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await _api.QueryDbAsync(db => db.Companies.FindAsync(employer.CompanyId).AsTask()));
        foreach (var key in keys)
        {
            Assert.False(await _api.FileExistsAsync(key), key);
        }
    }

    [Fact]
    public async Task AdminDeletingCompany_DeletesItsMediaFiles()
    {
        // firma bez członków i ofert z mediami (np. po odpięciu kont) - rekordy i pliki dodane bezpośrednio
        var company = await _api.CreateCompanyAsync();
        var photoKey = $"companies/{company.Id}/photos/{Guid.NewGuid():N}.png";
        var backgroundKey = $"companies/{company.Id}/offer-background/{Guid.NewGuid():N}.png";
        var storage = _api.Factory.Services.GetRequiredService<IFileStorage>();
        foreach (var key in new[] { photoKey, backgroundKey })
        {
            await storage.SaveAsync(key, new MemoryStream(Png()), "image/png");
        }

        await _api.QueryDbAsync(async db =>
        {
            db.CompanyPhotos.Add(new CompanyPhoto
            {
                CompanyId = company.Id, FileKey = photoKey, ContentType = "image/png", Size = 33, Width = 640, Height = 480,
                UploadedAt = DateTime.UtcNow
            });
            var entity = await db.Companies.FindAsync(company.Id);
            entity!.OfferBackgroundKey = backgroundKey;
            entity.OfferBackgroundContentType = "image/png";
            return await db.SaveChangesAsync();
        });

        using var admin = await _api.AdminClientAsync();
        var response = await admin.DeleteAsync($"/api/admin/companies/{company.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await _api.FileExistsAsync(photoKey));
        Assert.False(await _api.FileExistsAsync(backgroundKey));
        Assert.Empty(await _api.PhotoKeysAsync(company.Id));
    }
}
