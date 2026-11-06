using System.Text.Json;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Unit;

// Backend/appsettings.json: logi bez treści zapytań SQL (EF Core loguje każde polecenie na poziomie Information)
public class AppSettingsTests
{
    [Fact]
    public void SqlCommands_AreLoggedOnlyFromWarning()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Find(Path.Combine("Backend", "appsettings.json"))));
        var levels = settings.RootElement.GetProperty("Logging").GetProperty("LogLevel");

        Assert.Equal("Warning", levels.GetProperty("Microsoft.EntityFrameworkCore.Database.Command").GetString());
        Assert.Equal("Warning", levels.GetProperty("Microsoft.AspNetCore").GetString());
    }
}
