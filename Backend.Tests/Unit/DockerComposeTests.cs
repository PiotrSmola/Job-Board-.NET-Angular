using System.Text.RegularExpressions;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Unit;

// Lokalny stack demo ma znane hasła - porty mogą być publikowane tylko na localhost
public class DockerComposeTests
{
    // Zwraca wpisy z sekcji "ports:" wszystkich usług
    private static List<string> ReadPortMappings(string path)
    {
        var mappings = new List<string>();
        int? portsIndent = null;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;

            if (portsIndent.HasValue && indent > portsIndent.Value && trimmed.StartsWith('-'))
            {
                mappings.Add(trimmed.TrimStart('-').Trim().Trim('"', '\''));
                continue;
            }

            portsIndent = trimmed == "ports:" ? indent : null;
        }

        return mappings;
    }

    [Fact]
    public void AllPublishedPorts_AreBoundToLoopbackOnly()
    {
        var mappings = ReadPortMappings(RepositoryFiles.Find("docker-compose.yml"));

        Assert.Equal(3, mappings.Count);
        Assert.All(mappings, mapping => Assert.Matches(new Regex(@"^127\.0\.0\.1:\$\{[A-Z_]+:-\d+\}:\d+$"), mapping));
    }

    [Fact]
    public void Backend_EnablesDemoAccountsByDefaultWithOverride()
    {
        // lokalne demo w środowisku Production: konta demo tylko dzięki Seed:DemoAccounts, SEED_DEMO_ACCOUNTS=false je wyłącza
        var compose = File.ReadAllText(RepositoryFiles.Find("docker-compose.yml"));

        Assert.Contains("Seed__DemoAccounts: ${SEED_DEMO_ACCOUNTS:-true}", compose);
        Assert.Contains("SEED_DEMO_ACCOUNTS=true", File.ReadAllText(RepositoryFiles.Find(".env.example")));
    }

    [Fact]
    public void Storage_IsSeaweedFsWithS3AuthenticationAndNoPublishedPorts()
    {
        var compose = File.ReadAllText(RepositoryFiles.Find("docker-compose.yml"));
        var storage = ServiceBlock(compose, "storage");

        Assert.Contains("image: chrislusf/seaweedfs:4.50", storage);
        Assert.DoesNotContain("ports:", storage);
        Assert.Contains("- storage-data:/data", storage);
        Assert.Contains("healthcheck:", storage);
        // tożsamość S3 z kluczami z .env generowana przy starcie; $$ - zmienna kontenera, nie interpolacja compose
        Assert.Contains("\"$$S3_ACCESS_KEY\" \"$$S3_SECRET_KEY\" > /tmp/s3.json", storage);
        Assert.Contains("-s3.config=/tmp/s3.json", storage);
        Assert.Contains("exec /entrypoint.sh server -s3 -dir=/data", storage);
    }

    [Fact]
    public void Backend_UsesS3StorageServiceWithKeysFromEnv()
    {
        var compose = File.ReadAllText(RepositoryFiles.Find("docker-compose.yml"));
        var backend = ServiceBlock(compose, "backend");

        Assert.Contains("Storage__Provider: S3", backend);
        Assert.Contains("Storage__S3__ServiceUrl: http://storage:8333", backend);
        Assert.Contains("Storage__S3__AccessKey: \"${S3_ACCESS_KEY:?", backend);
        Assert.Contains("Storage__S3__SecretKey: \"${S3_SECRET_KEY:?", backend);
        Assert.Matches(new Regex(@"storage:\s+condition: service_healthy"), backend);

        var envExample = File.ReadAllText(RepositoryFiles.Find(".env.example"));
        Assert.Contains("S3_ACCESS_KEY=", envExample);
        Assert.Contains("S3_SECRET_KEY=", envExample);
    }

    [Fact]
    public void Backend_EnablesSwaggerByDefaultWithOverride()
    {
        // backend w środowisku Production udostępnia Swaggera tylko po jawnym Swagger:Enabled; SWAGGER_ENABLED=false wyłącza
        var backend = ServiceBlock(File.ReadAllText(RepositoryFiles.Find("docker-compose.yml")), "backend");

        Assert.Contains("ASPNETCORE_ENVIRONMENT: Production", backend);
        Assert.Contains("Swagger__Enabled: ${SWAGGER_ENABLED:-true}", backend);
        Assert.Contains("SWAGGER_ENABLED=true", File.ReadAllText(RepositoryFiles.Find(".env.example")));
    }

    [Fact]
    public void Backend_RequiresTheAdminPasswordFromEnv()
    {
        // hasło nowych kont adminów z .env (docker-up.ps1 je generuje) - bez niego compose nie wystartuje
        var backend = ServiceBlock(File.ReadAllText(RepositoryFiles.Find("docker-compose.yml")), "backend");

        Assert.Contains("Seed__AdminPassword: \"${ADMIN_PASSWORD:?ADMIN_PASSWORD is not set", backend);
        Assert.Contains("ADMIN_PASSWORD=", File.ReadAllText(RepositoryFiles.Find(".env.example")));
    }

    [Theory]
    [InlineData("db")]
    [InlineData("storage")]
    [InlineData("backend")]
    [InlineData("frontend")]
    public void EveryService_RunsWithNoNewPrivileges(string service)
    {
        var block = ServiceBlock(File.ReadAllText(RepositoryFiles.Find("docker-compose.yml")), service);

        Assert.Matches(new Regex(@"security_opt:\s+- no-new-privileges:true"), block);
    }

    // Backend/Dockerfile: aplikacja nie działa jako root, a katalog magazynu Local jest zapisywalny dla jej użytkownika
    [Fact]
    public void BackendImage_RunsAsTheAppUser()
    {
        var dockerfile = File.ReadAllText(RepositoryFiles.Find(Path.Combine("Backend", "Dockerfile"))).Replace("\r\n", "\n");
        var runtime = dockerfile[dockerfile.LastIndexOf("\nFROM ", StringComparison.Ordinal)..];

        var users = Regex.Matches(runtime, @"^USER\s+(\S+)", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal("app", users.Last());
        Assert.Contains("RUN mkdir -p /app/storage && chown app:app /app/storage", runtime);
        Assert.True(runtime.IndexOf("chown app:app", StringComparison.Ordinal) < runtime.IndexOf("USER app", StringComparison.Ordinal));
        Assert.Contains("ASPNETCORE_HTTP_PORTS=8080", runtime);
    }

    // Treść usługi (od "  name:" do następnej usługi albo sekcji najwyższego poziomu)
    private static string ServiceBlock(string compose, string service)
    {
        var lines = compose.Replace("\r\n", "\n").Split('\n');
        var start = Array.IndexOf(lines, $"  {service}:");
        Assert.True(start >= 0, $"service {service} not found");

        var block = lines.Skip(start + 1)
            .TakeWhile(line => line.Length == 0 || line.StartsWith("    ") || line.TrimStart().StartsWith('#'));
        return string.Join("\n", block);
    }
}
