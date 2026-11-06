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
}
