using System.Text.RegularExpressions;
using Backend.Helpers;
using Backend.Services;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Unit;

// frontend/docker/nginx.conf: 26 MB tylko dla uploadu załączników aplikacji i dokumentów profilu, reszta API z limitem
// 1 MB (jak backend)
public class NginxConfigTests
{
    private sealed record Location(string Modifier, string Pattern, string Body);

    private static List<Location> ReadLocations()
    {
        var config = File.ReadAllText(RepositoryFiles.Find(Path.Combine("frontend", "docker", "nginx.conf")));
        // bloki location bez zagnieżdżeń: "location [modyfikator] wzorzec { ... }"
        return Regex.Matches(config, @"^\s*location\s+(?:(~\*?|=|\^~)\s+)?(\S+)\s*\{(.*?)^\s*\}", RegexOptions.Multiline | RegexOptions.Singleline)
            .Select(m => new Location(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value))
            .ToList();
    }

    private static string? BodySizeLimit(Location location) =>
        Regex.Match(location.Body, @"^\s*client_max_body_size\s+(\S+);", RegexOptions.Multiline) is { Success: true } m
            ? m.Groups[1].Value
            : null;

    // lokalizacja, którą nginx wybierze dla ścieżki: pierwsza pasująca lokalizacja regex, inaczej najdłuższy prefiks
    private static Location Match(List<Location> locations, string path)
    {
        var regex = locations.FirstOrDefault(l => l.Modifier.StartsWith('~')
            && Regex.IsMatch(path, l.Pattern, l.Modifier == "~*" ? RegexOptions.IgnoreCase : RegexOptions.None));
        return regex ?? locations.Where(l => l.Modifier == "" && path.StartsWith(l.Pattern, StringComparison.Ordinal))
            .OrderByDescending(l => l.Pattern.Length)
            .First();
    }

    [Theory]
    [InlineData("/api/jobapplications/12/attachments")]
    [InlineData("/api/JobApplications/12/Attachments")]
    [InlineData("/api/jobapplications/12/attachments/")]
    public void AttachmentUpload_Allows26Megabytes(string path)
    {
        var location = Match(ReadLocations(), path);

        Assert.Equal("26m", BodySizeLimit(location));
        Assert.Equal(AttachmentFiles.MaxRequestSize, 26 * 1024 * 1024);
        Assert.Contains("proxy_pass http://backend:8080;", location.Body);
    }

    // POST /api/account/documents przyjmuje do 26 MB (3 x 5 MB itd.) - nginx z limitem 1 MB odrzuciłby w stosie
    // Dockera każdy plik profilu większy niż ~1 MB stroną HTML 413, a frontend pokazałby komunikat o limicie 5 MB
    [Theory]
    [InlineData("/api/account/documents")]
    [InlineData("/api/Account/Documents")]
    [InlineData("/api/account/documents/")]
    public void ProfileDocumentUpload_Allows26Megabytes(string path)
    {
        var location = Match(ReadLocations(), path);

        Assert.Equal("26m", BodySizeLimit(location));
        Assert.Equal(AttachmentFiles.MaxRequestSize, 26 * 1024 * 1024);
        Assert.Contains("proxy_pass http://backend:8080;", location.Body);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/account/documents/5")]
    [InlineData("/api/account/documentsx")]
    [InlineData("/api/account/documents/5/download")]
    [InlineData("/api/v2/account/documents")]
    [InlineData("/api/account/profile")]
    [InlineData("/api/jobapplications/12/attachments/from-profile")]
    [InlineData("/api/jobapplications")]
    [InlineData("/api/jobapplications/12/attachments/5")]
    [InlineData("/api/jobapplications/12/attachmentsx")]
    [InlineData("/api/jobs/1/attachments")]
    public void OtherApiRequests_AreLimitedToOneMegabyte(string path)
    {
        var location = Match(ReadLocations(), path);

        Assert.Equal("/api/", location.Pattern);
        Assert.Equal("1m", BodySizeLimit(location));
        Assert.Equal(RequestBodyLimitMiddleware.DefaultLimit, 1024 * 1024);
    }

    // każda lokalizacja uploadu (regex) przekazuje do backendu te same nagłówki co /api/ - inaczej backend widziałby
    // innego klienta (IP, schemat) zależnie od endpointu
    [Fact]
    public void AllApiLocations_ForwardTheSameProxyHeaders()
    {
        var locations = ReadLocations();
        var api = locations.Single(l => l.Pattern == "/api/");
        var uploads = locations.Where(l => l.Modifier.StartsWith('~')).ToList();

        static string[] ProxyLines(Location l) => l.Body.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("proxy_", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(uploads);
        Assert.All(uploads, upload => Assert.Equal(ProxyLines(api), ProxyLines(upload)));
        Assert.Contains("proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;", ProxyLines(api));
    }
}
