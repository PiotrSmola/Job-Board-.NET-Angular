using System.Net;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Nazwy plików CV przechodzące przez cały stack: sanityzacja przy uploadzie, zapis w bazie (utf8mb4, varchar(255))
// i nagłówek Content-Disposition przy pobraniu; rozszerzenie wynika z treści, nie z nazwy
[Collection(ApiCollection.Name)]
public class CvFileNameTests
{
    private const string Url = "/api/jobapplications";

    private readonly ApiFixture _api;
    private readonly CvScenario _cv;

    public CvFileNameTests(ApiFixture api)
    {
        _api = api;
        _cv = new CvScenario(api);
    }

    // Upload z nazwą przekazaną w filename* (UTF-8, procentowo), więc także znaki sterujące i formatujące
    private async Task<HttpResponseMessage> UploadWithNameAsync(CvSetup setup, string fileName, byte[]? content = null)
    {
        using var client = _api.CreateClient(setup.Candidate);
        return await client.PutAsync($"{Url}/{setup.Application.Id}/cv",
            CvTestFiles.RawForm(content ?? CvTestFiles.Pdf(), $"filename*=UTF-8''{Uri.EscapeDataString(fileName)}"));
    }

    private async Task<string> UploadedNameAsync(string fileName, byte[]? content = null)
    {
        var setup = await _cv.NewSetupAsync();
        var response = await UploadWithNameAsync(setup, fileName, content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var name = (await response.ReadJsonAsync<JobApplicationDto>()).CvFileName!;

        // ta sama nazwa wraca w nagłówku pobierania, a nagłówek jest jednowierszowy i bez surowych znaków sterujących
        using var client = _api.CreateClient(setup.Candidate);
        var download = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        var header = string.Join(",", download.Content.Headers.GetValues("Content-Disposition"));
        Assert.StartsWith("attachment; filename*=UTF-8''", header);
        Assert.All(header, c => Assert.True(c is >= ' ' and < '\u007f', $"Unexpected character U+{(int)c:X4} in Content-Disposition"));
        Assert.Equal(name, download.Content.Headers.ContentDisposition?.FileNameStar);
        return name;
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
    [InlineData("CON.pdf", "CON.pdf")]
    public async Task PolishAndUnicodeNames_SurviveUploadAndDownload(string sent, string expected)
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
    public async Task NamesWithoutAnyRealCharacters_FallBackToCv(string sent)
    {
        Assert.Equal("cv.pdf", await UploadedNameAsync(sent));
    }

    [Fact]
    public async Task TrailingDotsAndSpaces_AreTrimmed()
    {
        Assert.Equal("a.pdf", await UploadedNameAsync("a....pdf"));
        Assert.Equal("Jan Kowalski.pdf", await UploadedNameAsync("  Jan Kowalski  .pdf"));
        Assert.Equal("Jan Kowalski.pdf", await UploadedNameAsync("Jan Kowalski..."));
    }

    [Fact]
    public async Task VeryLongAsciiName_IsCutToMaxLengthKeepingTheExtension()
    {
        var name = await UploadedNameAsync(new string('a', 400) + ".pdf");

        Assert.Equal(CvFiles.MaxFileNameLength, name.Length);
        Assert.Equal(new string('a', 251) + ".pdf", name);
    }

    [Fact]
    public async Task VeryLongMultiByteName_FitsTheDatabaseColumnAndDownloadHeader()
    {
        var name = await UploadedNameAsync(new string('ż', 300) + ".docx", CvTestFiles.Docx());

        Assert.Equal(new string('ż', 250) + ".docx", name);
        Assert.Equal(CvFiles.MaxFileNameLength, name.Length);
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

        Assert.True(name.Length <= CvFiles.MaxFileNameLength);
        Assert.EndsWith(".pdf", name);
        Assert.StartsWith(new string('a', 250), name);
    }

    [Fact]
    public async Task AbsurdlyLongName_IsRejectedOrCutButNeverFailsWithServerError()
    {
        var setup = await _cv.NewSetupAsync();

        var response = await UploadWithNameAsync(setup, new string('x', 20_000) + ".pdf");

        // nagłówki części multipart są limitowane (4 KB) - zbyt długa nazwa to błędny formularz, nie błąd serwera
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest, $"Unexpected {response.StatusCode}");
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            Assert.Equal("Invalid multipart form data", await response.ReadMessageAsync());
            Assert.Null(await _cv.StoredKeyAsync(setup.Application.Id));
            Assert.Empty(_cv.StoredFilesOf(setup.Application.Id));
        }
    }

    [Theory]
    [InlineData("cv.docx", "cv.pdf")]
    [InlineData("cv.DOCX", "cv.pdf")]
    [InlineData("cv.doc", "cv.pdf")]
    [InlineData("cv", "cv.pdf")]
    [InlineData("cv.exe", "cv.exe.pdf")]
    [InlineData("cv.txt", "cv.txt.pdf")]
    [InlineData("cv.png", "cv.png.pdf")]
    [InlineData("Jan.Kowalski CV", "Jan.Kowalski CV.pdf")]
    public async Task PdfContentWithOtherExtension_IsStoredAsPdf(string sent, string expected)
    {
        var setup = await _cv.NewSetupAsync();

        var response = await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), sent);

        Assert.Equal(expected, response.CvFileName);
        Assert.Matches($"^cv/{setup.Application.Id}/[0-9a-f]{{32}}\\.pdf$", await _cv.StoredKeyAsync(setup.Application.Id));
        Assert.Equal("application/pdf", (await _cv.CvRecordAsync(setup.Application.Id)).ContentType);
    }

    [Theory]
    [InlineData("cv.pdf", "cv.docx")]
    [InlineData("résumé.PDF", "résumé.docx")]
    [InlineData("cv.doc", "cv.docx")]
    [InlineData("cv.docx", "cv.docx")]
    [InlineData("cv", "cv.docx")]
    public async Task DocxContentWithOtherExtension_IsStoredAsDocx(string sent, string expected)
    {
        var setup = await _cv.NewSetupAsync();

        var response = await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Docx(), sent);

        Assert.Equal(expected, response.CvFileName);
        Assert.Matches($"^cv/{setup.Application.Id}/[0-9a-f]{{32}}\\.docx$", await _cv.StoredKeyAsync(setup.Application.Id));
        Assert.Equal(CvTestFiles.DocxContentType, (await _cv.CvRecordAsync(setup.Application.Id)).ContentType);
    }

    [Fact]
    public async Task ClientContentType_IsIgnored_ServerUsesTheDetectedOne()
    {
        var setup = await _cv.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        var pdfAsHtml = await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Pdf(), "cv.pdf", "text/html");
        Assert.Equal(HttpStatusCode.OK, pdfAsHtml.StatusCode);
        var download = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);

        var docxAsImage = await client.UploadCvAsync(setup.Application.Id, CvTestFiles.Docx(), "cv.docx", "image/png");
        Assert.Equal(HttpStatusCode.OK, docxAsImage.StatusCode);
        download = await client.GetAsync($"{Url}/{setup.Application.Id}/cv");
        Assert.Equal(CvTestFiles.DocxContentType, download.Content.Headers.ContentType?.MediaType);

        // odwrotnie: poprawny nagłówek nie ratuje podróbki
        var fake = await client.UploadCvAsync(setup.Application.Id, "<html><script>alert(1)</script></html>"u8.ToArray(), "cv.pdf", CvTestFiles.PdfContentType);
        Assert.Equal(HttpStatusCode.BadRequest, fake.StatusCode);
    }

    [Fact]
    public async Task StorageKey_NeverContainsAnythingFromTheFileName()
    {
        var setup = await _cv.NewSetupAsync();

        await UploadWithNameAsync(setup, "../../../etc/passwd;Życiorys\u202E.pdf");

        var key = (await _cv.StoredKeyAsync(setup.Application.Id))!;
        Assert.Matches($"^cv/{setup.Application.Id}/[0-9a-f]{{32}}\\.pdf$", key);
        Assert.DoesNotContain("passwd", key);
        var stored = Assert.Single(_cv.AllStoredFiles(), f => f.Contains(Path.Combine("cv", setup.Application.Id.ToString())));
        Assert.StartsWith(Path.GetFullPath(_api.Factory.StoragePath), Path.GetFullPath(stored));
    }

    [Fact]
    public async Task EmptyFileNameInMultipart_IsNotTreatedAsFile()
    {
        var setup = await _cv.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        var response = await client.PutAsync($"{Url}/{setup.Application.Id}/cv", CvTestFiles.RawForm(CvTestFiles.Pdf(), "filename=\"\""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await _cv.StoredKeyAsync(setup.Application.Id));
        Assert.Empty(_cv.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task FilenameStarWithInvalidEncoding_DoesNotCauseServerError()
    {
        var setup = await _cv.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        // nieprawidłowe sekwencje UTF-8 (%FF%FE) i urwane kodowanie procentowe
        foreach (var parameters in new[] { "filename*=UTF-8''%FF%FEcv.pdf", "filename*=UTF-8''cv%2", "filename*=NOPE''cv.pdf", "filename*=" })
        {
            var response = await client.PutAsync($"{Url}/{setup.Application.Id}/cv", CvTestFiles.RawForm(CvTestFiles.Pdf(), parameters));
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
