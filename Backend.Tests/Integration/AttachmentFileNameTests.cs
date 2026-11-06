using System.Net;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Nazwy plików załączników przechodzące przez cały stack: sanityzacja przy uploadzie, zapis w bazie (utf8mb4,
// varchar(255)) i nagłówek Content-Disposition przy pobraniu; rozszerzenie wynika z treści, nie z nazwy
[Collection(ApiCollection.Name)]
public class AttachmentFileNameTests
{
    private readonly ApiFixture _api;
    private readonly AttachmentScenario _attachments;

    public AttachmentFileNameTests(ApiFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
    }

    // Upload z nazwą przekazaną w filename* (UTF-8, procentowo), więc także znaki sterujące i formatujące
    private async Task<HttpResponseMessage> UploadWithNameAsync(AttachmentSetup setup, string fileName, byte[]? content = null)
    {
        using var client = _api.CreateClient(setup.Candidate);
        return await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id),
            AttachmentTestFiles.RawForm(content ?? AttachmentTestFiles.Pdf(), $"filename*=UTF-8''{Uri.EscapeDataString(fileName)}"));
    }

    private async Task<string> UploadedNameAsync(string fileName, byte[]? content = null)
    {
        var setup = await _attachments.NewSetupAsync();
        var response = await UploadWithNameAsync(setup, fileName, content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var attachment = Assert.Single((await response.ReadJsonAsync<JobApplicationDto>()).Attachments);

        // ta sama nazwa wraca w nagłówku pobierania, a nagłówek jest jednowierszowy i bez surowych znaków sterujących
        using var client = _api.CreateClient(setup.Candidate);
        var download = await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        var header = string.Join(",", download.Content.Headers.GetValues("Content-Disposition"));
        Assert.StartsWith("attachment; filename*=UTF-8''", header);
        Assert.All(header, c => Assert.True(c is >= ' ' and < '\u007f', $"Unexpected character U+{(int)c:X4} in Content-Disposition"));
        Assert.Equal(attachment.FileName, download.Content.Headers.ContentDisposition?.FileNameStar);
        return attachment.FileName;
    }

    [Theory]
    [InlineData("Życiorys Łukasz Wójcik – zażółć gęślą jaźń.pdf", "Życiorys Łukasz Wójcik – zażółć gęślą jaźń.pdf")]
    [InlineData("ĄĆĘŁŃÓŚŹŻ ąćęłńóśźż.pdf", "ĄĆĘŁŃÓŚŹŻ ąćęłńóśźż.pdf")]
    [InlineData("Jose\u0301 Zo\u0301ła.pdf", "José Zóła.pdf")] // NFD -> NFC
    [InlineData("日本語の履歴書.pdf", "日本語の履歴書.pdf")]
    [InlineData("CV 😀 Jan.pdf", "CV 😀 Jan.pdf")]
    [InlineData("Jan Kowalski (2026) [final] #1 100%.pdf", "Jan Kowalski (2026) [final] #1 100%.pdf")]
    [InlineData("C:\\fakepath\\Moje CV.pdf", "Moje CV.pdf")]
    [InlineData("..\\..\\windows\\system32\\cv.pdf", "cv.pdf")]
    [InlineData("a\";b.pdf", "a_;b.pdf")]
    [InlineData("CONSOLE.pdf", "CONSOLE.pdf")]
    [InlineData("COM10.pdf", "COM10.pdf")]
    public async Task PolishAndUnicodeNames_SurviveUploadAndDownload(string sent, string expected)
    {
        Assert.Equal(expected, await UploadedNameAsync(sent));
    }

    [Theory]
    [InlineData("CON.pdf", "_CON.pdf")]
    [InlineData("con.pdf", "_con.pdf")]
    [InlineData("Nul", "_Nul.pdf")]
    [InlineData("aux.tar.pdf", "_aux.tar.pdf")]
    [InlineData("PRN .pdf", "_PRN.pdf")]
    [InlineData("COM1.pdf", "_COM1.pdf")]
    [InlineData("lpt9.docx", "_lpt9.pdf")]
    public async Task WindowsReservedNames_GetAnUnderscorePrefix(string sent, string expected)
    {
        Assert.Equal(expected, await UploadedNameAsync(sent));
    }

    [Theory]
    [InlineData("cv\u202Eexe.pdf", "cvexe.pdf")] // odwrócenie kierunku tekstu (RLO)
    [InlineData("\u202Etxt.fdp", "txt.fdp.pdf")] // klasyczne "exe.pdf" udające inne rozszerzenie
    [InlineData("a\u202Ab\u202Cc\u2066d\u2069e.pdf", "abcde.pdf")]
    [InlineData("a\u200Eb\u200Fc\u200Bd\uFEFFe\u00ADf.pdf", "abcdef.pdf")]
    [InlineData("tab\there\u0000nul\u0085nel\u2028ls\u2029ps.pdf", "tabherenulnellsps.pdf")]
    public async Task DirectionalAndControlCharacters_AreRemoved(string sent, string expected)
    {
        Assert.Equal(expected, await UploadedNameAsync(sent));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("....")]
    [InlineData(".pdf")]
    [InlineData("....pdf")]
    [InlineData(" ")]
    [InlineData("/")]
    [InlineData("\\")]
    [InlineData("\u202E")]
    [InlineData(". . .")]
    [InlineData(" . . . .pdf")]
    [InlineData(".\u202E. \t.")]
    public async Task NamesWithoutAnyRealCharacters_FallBackToAttachment(string sent)
    {
        Assert.Equal(AttachmentFiles.FallbackFileName + ".pdf", await UploadedNameAsync(sent));
    }

    [Fact]
    public async Task TrailingDotsAndSpaces_AreTrimmed()
    {
        Assert.Equal("a.pdf", await UploadedNameAsync("a....pdf"));
        Assert.Equal("Jan Kowalski.pdf", await UploadedNameAsync("  Jan Kowalski  .pdf"));
        Assert.Equal("Jan Kowalski.pdf", await UploadedNameAsync("Jan Kowalski..."));
        // spacje i kropki przemieszane na końcu - także po usunięciu znaków sterujących między nimi
        Assert.Equal("Jan.pdf", await UploadedNameAsync("Jan . . ."));
        Assert.Equal("Jan.pdf", await UploadedNameAsync("Jan .\u0007 .\u202E ."));
    }

    [Fact]
    public async Task VeryLongAsciiName_IsCutToMaxLengthKeepingTheExtension()
    {
        var name = await UploadedNameAsync(new string('a', 400) + ".pdf");

        Assert.Equal(AttachmentFiles.MaxFileNameLength, name.Length);
        Assert.Equal(new string('a', 251) + ".pdf", name);
    }

    [Fact]
    public async Task VeryLongMultiByteName_FitsTheDatabaseColumnAndDownloadHeader()
    {
        var name = await UploadedNameAsync(new string('ż', 300) + ".docx", AttachmentTestFiles.Docx());

        Assert.Equal(new string('ż', 250) + ".docx", name);
        Assert.Equal(AttachmentFiles.MaxFileNameLength, name.Length);
    }

    [Fact]
    public async Task LongNameCutInsideSurrogatePair_DoesNotLeaveALoneSurrogate()
    {
        // 250 znaków + dwa emoji (po 2 jednostki UTF-16): granica 251 wypada w środku drugiego znaku pary
        var name = await UploadedNameAsync(new string('a', 250) + "😀😀.pdf");

        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsHighSurrogate(name[i]))
            {
                Assert.True(i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]), "Broken surrogate pair");
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(name[i]), "Lone low surrogate");
            }
        }

        Assert.True(name.Length <= AttachmentFiles.MaxFileNameLength);
        Assert.EndsWith(".pdf", name);
        Assert.StartsWith(new string('a', 250), name);
    }

    [Fact]
    public async Task AbsurdlyLongName_IsRejectedOrCutButNeverFailsWithServerError()
    {
        var setup = await _attachments.NewSetupAsync();

        var response = await UploadWithNameAsync(setup, new string('x', 20_000) + ".pdf");

        // nagłówki części multipart są limitowane (4 KB) - zbyt długa nazwa to błędny formularz, nie błąd serwera
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.BadRequest, $"Unexpected {response.StatusCode}");
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            Assert.Equal(AttachmentFiles.InvalidFormMessage, await response.ReadMessageAsync());
            Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
            Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
        }
    }

    [Theory]
    [InlineData("cv.docx", "cv.pdf")]
    [InlineData("cv.DOCX", "cv.pdf")]
    [InlineData("cv.doc", "cv.pdf")]
    [InlineData("cv.png", "cv.pdf")]
    [InlineData("cv.JPEG", "cv.pdf")]
    [InlineData("cv", "cv.pdf")]
    [InlineData("cv.exe", "cv.exe.pdf")]
    [InlineData("cv.txt", "cv.txt.pdf")]
    [InlineData("cv.gif", "cv.gif.pdf")]
    [InlineData("Jan.Kowalski CV", "Jan.Kowalski CV.pdf")]
    public async Task PdfContentWithOtherExtension_IsStoredAsPdf(string sent, string expected)
    {
        var setup = await _attachments.NewSetupAsync();

        var response = await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), sent);

        Assert.Equal(expected, Assert.Single(response.Attachments).FileName);
        var record = Assert.Single(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Matches($"^attachments/{setup.Application.Id}/[0-9a-f]{{32}}\\.pdf$", record.Key);
        Assert.Equal("application/pdf", record.ContentType);
    }

    [Theory]
    [InlineData("cv.pdf", "cv.docx")]
    [InlineData("résumé.PDF", "résumé.docx")]
    [InlineData("cv.doc", "cv.docx")]
    [InlineData("cv.docx", "cv.docx")]
    [InlineData("cv.jpg", "cv.docx")]
    [InlineData("cv", "cv.docx")]
    public async Task DocxContentWithOtherExtension_IsStoredAsDocx(string sent, string expected)
    {
        var setup = await _attachments.NewSetupAsync();

        var response = await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Docx(), sent);

        Assert.Equal(expected, Assert.Single(response.Attachments).FileName);
        var record = Assert.Single(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Matches($"^attachments/{setup.Application.Id}/[0-9a-f]{{32}}\\.docx$", record.Key);
        Assert.Equal(AttachmentTestFiles.DocxContentType, record.ContentType);
    }

    [Fact]
    public async Task ClientContentType_IsIgnored_ServerUsesTheDetectedOne()
    {
        var setup = await _attachments.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        async Task<string?> UploadAndDownloadTypeAsync(byte[] content, string name, string clientType)
        {
            var upload = await client.UploadAttachmentAsync(setup.Application.Id, content, name, clientType);
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            var attachment = (await upload.ReadJsonAsync<JobApplicationDto>()).Attachments.Single(a => a.FileName == name);
            var download = await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id);
            return download.Content.Headers.ContentType?.MediaType;
        }

        Assert.Equal("application/pdf", await UploadAndDownloadTypeAsync(AttachmentTestFiles.Pdf(), "a.pdf", "text/html"));
        Assert.Equal(AttachmentTestFiles.DocxContentType, await UploadAndDownloadTypeAsync(AttachmentTestFiles.Docx(), "b.docx", "image/png"));
        Assert.Equal("image/png", await UploadAndDownloadTypeAsync(AttachmentTestFiles.Png(), "c.png", "image/svg+xml"));
        Assert.Equal("image/jpeg", await UploadAndDownloadTypeAsync(AttachmentTestFiles.Jpeg(), "d.jpg", "text/html"));

        // odwrotnie: poprawny nagłówek nie ratuje podróbki
        var fake = await client.UploadAttachmentAsync(setup.Application.Id, "<html><script>alert(1)</script></html>"u8.ToArray(), "cv.pdf", AttachmentTestFiles.PdfContentType);
        Assert.Equal(HttpStatusCode.BadRequest, fake.StatusCode);
        var svg = await client.UploadAttachmentAsync(setup.Application.Id, "<svg onload=\"alert(1)\"/>"u8.ToArray(), "x.png", AttachmentTestFiles.PngContentType);
        Assert.Equal(HttpStatusCode.BadRequest, svg.StatusCode);
    }

    [Fact]
    public async Task StorageKey_NeverContainsAnythingFromTheFileName()
    {
        var setup = await _attachments.NewSetupAsync();

        await UploadWithNameAsync(setup, "../../../etc/passwd;Życiorys\u202E.pdf");

        var key = Assert.Single(await _attachments.StoredKeysAsync(setup.Application.Id));
        Assert.Matches($"^attachments/{setup.Application.Id}/[0-9a-f]{{32}}\\.pdf$", key);
        Assert.DoesNotContain("passwd", key);
        var stored = Assert.Single(_attachments.StoredFilesOf(setup.Application.Id));
        Assert.StartsWith(Path.GetFullPath(_api.Factory.StoragePath), Path.GetFullPath(stored));
    }

    [Fact]
    public async Task SameNameUploadedTwice_KeepsBothFilesUnderDifferentKeys()
    {
        var setup = await _attachments.NewSetupAsync();

        var dto = await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            new AttachmentUpload(AttachmentTestFiles.Pdf(1), "cv.pdf"), new AttachmentUpload(AttachmentTestFiles.Pdf(2), "cv.pdf"));

        Assert.Equal(new[] { "cv.pdf", "cv.pdf" }, dto.Attachments.Select(a => a.FileName));
        var keys = await _attachments.StoredKeysAsync(setup.Application.Id);
        Assert.Equal(2, keys.Distinct().Count());
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Fact]
    public async Task EmptyFileNameInMultipart_IsNotTreatedAsFile()
    {
        var setup = await _attachments.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        var response = await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id),
            AttachmentTestFiles.RawForm(AttachmentTestFiles.Pdf(), "filename=\"\""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task FilenameStarWithInvalidEncoding_DoesNotCauseServerError()
    {
        var setup = await _attachments.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        // nieprawidłowe sekwencje UTF-8 (%FF%FE) i urwane kodowanie procentowe
        foreach (var parameters in new[] { "filename*=UTF-8''%FF%FEcv.pdf", "filename*=UTF-8''cv%2", "filename*=NOPE''cv.pdf", "filename*=" })
        {
            var response = await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id),
                AttachmentTestFiles.RawForm(AttachmentTestFiles.Pdf(), parameters));
            Assert.True((int)response.StatusCode < 500, $"{parameters} -> {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task NameNormalization_DoesNotBreakDownloadForSpecialCharactersInHeader()
    {
        var name = await UploadedNameAsync("a%b'c(d)e,f;g=h@i+j~k!l$m&n{o}p^q`r.pdf");

        Assert.Equal("a%b'c(d)e,f;g=h@i+j~k!l$m&n{o}p^q`r.pdf", name);
    }
}
