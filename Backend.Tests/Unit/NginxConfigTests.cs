using System.Text.RegularExpressions;
using Backend.Helpers;
using Backend.Services;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Unit;

// frontend/docker/nginx.conf: 26 MB tylko dla uploadu załączników aplikacji, dokumentów profilu i mediów firmy, reszta API z limitem
// 1 MB (jak backend); nagłówki bezpieczeństwa (CSP), kompresja i Cache-Control na poziomie server
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

    // POST zdjęć firmy (do 20 plików po 5 MB w limicie żądania 26 MB) i PUT obrazu tła ofert
    [Theory]
    [InlineData("/api/employer/company/photos")]
    [InlineData("/api/Employer/Company/Photos")]
    [InlineData("/api/employer/company/photos/")]
    [InlineData("/api/employer/company/offer-background")]
    [InlineData("/api/employer/company/offer-background/")]
    public void CompanyMediaUpload_Allows26Megabytes(string path)
    {
        var location = Match(ReadLocations(), path);

        Assert.Equal("26m", BodySizeLimit(location));
        Assert.Equal(AttachmentFiles.MaxRequestSize, 26 * 1024 * 1024);
        Assert.Contains("proxy_pass http://backend:8080;", location.Body);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/employer/company")]
    [InlineData("/api/employer/company/photos/order")]
    [InlineData("/api/employer/company/photos/5")]
    [InlineData("/api/employer/company/photosx")]
    [InlineData("/api/employer/company/offer-backgroundx")]
    [InlineData("/api/companies/5/photos")]
    [InlineData("/api/admin/companies/5/photos/2")]
    [InlineData("/api/v2/employer/company/photos")]
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
        // nosniff dodaje nginx - bez drugiej kopii z backendu
        Assert.Contains("proxy_hide_header X-Content-Type-Options;", ProxyLines(api));
    }

    // --- nagłówki bezpieczeństwa, kompresja i cache (blok server) ---

    private static string ReadConfig() => File.ReadAllText(RepositoryFiles.Find(Path.Combine("frontend", "docker", "nginx.conf")));

    // dyrektywy bloku server poza blokami location (bez komentarzy), z ujednoliconymi odstępami
    private static List<string> ServerLevelDirectives()
    {
        var config = Regex.Replace(ReadConfig(), @"#[^\n]*", string.Empty);
        var server = Regex.Match(config, @"^server\s*\{(.*)^\}", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
        var withoutLocations = Regex.Replace(server, @"^\s*location\s[^{]*\{.*?^\s*\}", string.Empty,
            RegexOptions.Multiline | RegexOptions.Singleline);
        // średnik kończy dyrektywę, chyba że jest w cudzysłowie (np. w wartości CSP)
        return Regex.Matches(withoutLocations, "(?:\"[^\"]*\"|[^;\"])+;")
            .Select(m => Regex.Replace(m.Value.TrimEnd(';').Trim(), @"\s+", " "))
            .Where(d => d.Length > 0)
            .ToList();
    }

    // add_header z poziomu server: nazwa -> wartość (z sufiksem " always", jeśli jest)
    private static Dictionary<string, string> ServerHeaders() =>
        ServerLevelDirectives()
            .Select(d => Regex.Match(d, "^add_header (\\S+) (\"[^\"]*\"|\\S+)( always)?$"))
            .Where(m => m.Success)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim('"') + m.Groups[3].Value);

    // add_header w bloku location kasuje dziedziczenie wszystkich nagłówków z poziomu server
    [Fact]
    public void NoLocation_DefinesItsOwnHeaders()
    {
        Assert.All(ReadLocations(), l => Assert.DoesNotContain("add_header", l.Body));
        Assert.Contains(ServerLevelDirectives(), d => d.StartsWith("add_header ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Referrer-Policy", "strict-origin-when-cross-origin")]
    [InlineData("Cross-Origin-Opener-Policy", "same-origin")]
    public void SecurityHeaders_AreSentWithEveryResponse(string header, string value)
    {
        Assert.Equal($"{value} always", ServerHeaders()[header]);
    }

    [Fact]
    public void PermissionsPolicy_DisablesDeviceFeatures()
    {
        var policy = ServerHeaders()["Permissions-Policy"];

        Assert.EndsWith(" always", policy);
        foreach (var feature in new[] { "camera=()", "microphone=()", "geolocation=()" })
        {
            Assert.Contains(feature, policy);
        }
    }

    [Fact]
    public void ContentSecurityPolicy_AllowsOnlyOwnScriptsAndGoogleFonts()
    {
        var csp = ServerHeaders()["Content-Security-Policy"];
        Assert.EndsWith(" always", csp);
        var directives = csp[..^" always".Length]
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToDictionary(d => d.Split(' ')[0], d => d);

        Assert.Equal("default-src 'self'", directives["default-src"]);
        // bez 'unsafe-inline' i 'unsafe-eval' - build nie ma inline skryptów (inlineCritical: false w angular.json)
        Assert.Equal("script-src 'self'", directives["script-src"]);
        Assert.Equal("style-src 'self' 'unsafe-inline' https://fonts.googleapis.com", directives["style-src"]);
        Assert.Equal("font-src 'self' https://fonts.gstatic.com", directives["font-src"]);
        Assert.Equal("img-src 'self' data: blob:", directives["img-src"]);
        Assert.Equal("connect-src 'self'", directives["connect-src"]);
        Assert.Equal("object-src 'none'", directives["object-src"]);
        Assert.Equal("base-uri 'self'", directives["base-uri"]);
        Assert.Equal("form-action 'self'", directives["form-action"]);
        Assert.Equal("frame-ancestors 'none'", directives["frame-ancestors"]);
    }

    // CSP bez inline skryptów wymaga buildu bez skryptu przełączającego krytyczny CSS (Beasties wstawia go inline)
    [Fact]
    public void ProductionBuild_DoesNotInlineCriticalCss()
    {
        using var angular = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Find(Path.Combine("frontend", "angular.json"))));
        var production = angular.RootElement.GetProperty("projects").GetProperty("frontend").GetProperty("architect")
            .GetProperty("build").GetProperty("configurations").GetProperty("production");

        Assert.False(production.GetProperty("optimization").GetProperty("styles").GetProperty("inlineCritical").GetBoolean());
        Assert.Equal("all", production.GetProperty("outputHashing").GetString());
    }

    [Fact]
    public void ServerTokensAndGzip_AreConfigured()
    {
        var directives = ServerLevelDirectives();

        Assert.Contains("server_tokens off", directives);
        Assert.Contains("gzip on", directives);
        Assert.Contains("gzip_types text/css application/javascript application/json image/svg+xml text/plain", directives);
        Assert.Contains("gzip_min_length 1024", directives);
        Assert.Contains("gzip_vary on", directives);
    }

    // map $uri -> Cache-Control (jak nginx: pierwszy pasujący regex w kolejności z pliku, inaczej default)
    private static string CacheControlFor(string uri)
    {
        var map = Regex.Match(ReadConfig(), @"^map \$uri \$static_cache_control \{(.*?)^\}", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(map.Success, "map $uri $static_cache_control not found");
        Assert.Equal("$static_cache_control", ServerHeaders()["Cache-Control"]);

        string? fallback = null;
        foreach (Match entry in Regex.Matches(map.Groups[1].Value, "^\\s*(\"[^\"]*\"|\\S+)\\s+\"([^\"]*)\";", RegexOptions.Multiline))
        {
            var key = entry.Groups[1].Value.Trim('"');
            if (key == "default")
            {
                fallback = entry.Groups[2].Value;
                continue;
            }

            Assert.StartsWith("~", key);
            var ignoreCase = key.StartsWith("~*", StringComparison.Ordinal);
            if (Regex.IsMatch(uri, key[(ignoreCase ? 2 : 1)..], ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None))
            {
                return entry.Groups[2].Value;
            }
        }

        Assert.NotNull(fallback);
        return fallback!;
    }

    [Theory]
    [InlineData("/main-AWZKRXVT.js")]
    [InlineData("/chunk-BZUlmxhP.js")]
    [InlineData("/chunk-D7HnCm-l.js")]
    [InlineData("/chunk-D2T_dVZZ.js")]
    [InlineData("/styles-XAC2Q3T5.css")]
    [InlineData("/media/roboto-latin-ABCD1234.woff2")]
    public void HashedAssets_AreCachedForAYear(string uri)
    {
        Assert.Equal("public, max-age=31536000, immutable", CacheControlFor(uri));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/favicon.ico")]
    [InlineData("/jobs/12")]
    [InlineData("/polyfills.js")]
    public void IndexAndUnhashedFiles_AreRevalidated(string uri)
    {
        Assert.Equal("no-cache", CacheControlFor(uri));
    }

    [Theory]
    [InlineData("/api/jobs")]
    [InlineData("/api/jobapplications/1/attachments/2")]
    // obrazy firm: Cache-Control (publiczny tylko dla firm zatwierdzonych) ustawia backend
    [InlineData("/api/companies/5/photos/2/file")]
    [InlineData("/api/companies/5/offer-background/file")]
    [InlineData("/api/files/report-ABCD1234.js")]
    public void ApiResponses_GetNoCacheControlFromNginx(string uri)
    {
        Assert.Equal(string.Empty, CacheControlFor(uri));
    }
}
