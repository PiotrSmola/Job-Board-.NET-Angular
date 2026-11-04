using System.Text.RegularExpressions;

namespace Backend.Tests.Unit;

// Lokalny stack demo ma znane hasła - porty mogą być publikowane tylko na localhost
public class DockerComposeTests
{
    private static string FindComposeFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "docker-compose.yml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("docker-compose.yml not found in any parent directory of the test assembly");
    }

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
        var mappings = ReadPortMappings(FindComposeFile());

        Assert.Equal(3, mappings.Count);
        Assert.All(mappings, mapping => Assert.Matches(new Regex(@"^127\.0\.0\.1:\$\{[A-Z_]+:-\d+\}:\d+$"), mapping));
    }
}
