namespace Backend.Tests.Infrastructure;

// Pliki z katalogu głównego repozytorium (docker-compose.yml, docker-up.ps1) - szukane w górę od katalogu testów
public static class RepositoryFiles
{
    public static string Find(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{fileName} not found in any parent directory of the test assembly");
    }
}
