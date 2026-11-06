using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Unit;

// docker-up.ps1 nie jest uruchamiany - test wyciąga z niego samą funkcję Get-EnvValue (parser PowerShella)
// i wywołuje ją w katalogu tymczasowym z przygotowanym plikiem .env. Wymaga pwsh (jest w obrazie .NET SDK)
// albo Windows PowerShell.
public class DockerUpScriptTests
{
    private const string Harness = """
        param([string]$ScriptPath)
        $tokens = $null; $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref]$tokens, [ref]$errors)
        if ($errors.Count -gt 0) { throw "docker-up.ps1 has parse errors: $($errors -join '; ')" }
        $function = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-EnvValue' }, $true) | Select-Object -First 1
        Invoke-Expression $function.Extent.Text
        "DB_PASSWORD=$(Get-EnvValue 'DB_PASSWORD' 'jobboard')"
        "JWT_SECRET=$(Get-EnvValue 'JWT_SECRET' '')"
        "ALLOW_SEED_REFRESH=$(Get-EnvValue 'ALLOW_SEED_REFRESH' 'default')"
        "SEED_DEMO_ACCOUNTS=$(Get-EnvValue 'SEED_DEMO_ACCOUNTS' 'default')"
        "MISSING=$(Get-EnvValue 'MISSING' 'fallback')"
        """;

    private static Dictionary<string, string> RunGetEnvValue(string envFileContent)
    {
        var directory = Directory.CreateTempSubdirectory("docker-up-test-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, ".env"), envFileContent);
            var harnessPath = Path.Combine(directory.FullName, "harness.ps1");
            File.WriteAllText(harnessPath, Harness, new UTF8Encoding(true));

            var shell = OperatingSystem.IsWindows() && !CanStart("pwsh") ? "powershell" : "pwsh";
            var startInfo = new ProcessStartInfo(shell)
            {
                WorkingDirectory = directory.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", harnessPath, RepositoryFiles.Find("docker-up.ps1") })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"PowerShell failed: {error}");

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split('=', 2))
                .ToDictionary(parts => parts[0], parts => parts[1]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static bool CanStart(string fileName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, "-NoProfile -Command exit") { RedirectStandardOutput = true });
            process?.WaitForExit();
            return process != null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [Fact]
    public void GetEnvValue_WithEmptyValues_FallsBackToDefaults()
    {
        var values = RunGetEnvValue("DB_PASSWORD=\nJWT_SECRET=\nALLOW_SEED_REFRESH=  \nSEED_DEMO_ACCOUNTS=\n");

        Assert.Equal("jobboard", values["DB_PASSWORD"]);
        Assert.Equal(string.Empty, values["JWT_SECRET"]);
        Assert.Equal("default", values["ALLOW_SEED_REFRESH"]);
        Assert.Equal("default", values["SEED_DEMO_ACCOUNTS"]);
        Assert.Equal("fallback", values["MISSING"]);
    }

    [Fact]
    public void GetEnvValue_WithValues_KeepsThem()
    {
        var values = RunGetEnvValue("DB_PASSWORD=s3cret\r\nJWT_SECRET=abc=def\r\nALLOW_SEED_REFRESH=false\r\nSEED_DEMO_ACCOUNTS=false\r\n");

        Assert.Equal("s3cret", values["DB_PASSWORD"]);
        Assert.Equal("abc=def", values["JWT_SECRET"]);
        Assert.Equal("false", values["ALLOW_SEED_REFRESH"]);
        Assert.Equal("false", values["SEED_DEMO_ACCOUNTS"]);
    }

    [Fact]
    public void Script_KeepsSeedDemoAccountsFromPreviousEnvFile()
    {
        // .env jest przepisywany przy każdym uruchomieniu - wartość ustawiona przez użytkownika nie może zginąć
        var script = File.ReadAllText(RepositoryFiles.Find("docker-up.ps1"));

        Assert.Contains("Get-EnvValue 'SEED_DEMO_ACCOUNTS'", script);
        Assert.Contains("\"SEED_DEMO_ACCOUNTS=$seedDemoAccounts\"", script);
    }

    [Fact]
    public void Script_GeneratesAndKeepsS3Keys()
    {
        // klucze magazynu plików: zachowane z .env, generowane (20 i 40 znaków) tylko gdy brak, ustawione w env dla "down"
        var script = File.ReadAllText(RepositoryFiles.Find("docker-up.ps1"));

        Assert.Contains("Get-EnvValue 'S3_ACCESS_KEY'", script);
        Assert.Contains("Get-EnvValue 'S3_SECRET_KEY'", script);
        Assert.Contains("New-RandomSecret 20", script);
        Assert.Contains("New-RandomSecret 40", script);
        Assert.Contains("\"S3_ACCESS_KEY=$s3AccessKey\"", script);
        Assert.Contains("\"S3_SECRET_KEY=$s3SecretKey\"", script);
        Assert.Contains("$env:S3_ACCESS_KEY = $s3AccessKey", script);
        Assert.Contains("Env:S3_SECRET_KEY", script);
    }

    [Fact]
    public void Script_ReplacesExistingS3KeysThatAreNotLongAlphanumericStrings()
    {
        // klucze trafiają bez escapowania do JSON-a konfiguracji SeaweedFS - inne niż [A-Za-z0-9]{16,} są wymieniane
        var script = File.ReadAllText(RepositoryFiles.Find("docker-up.ps1"));
        var match = Regex.Match(script, @"\$s3KeyPattern = '([^']+)'");
        Assert.True(match.Success, "docker-up.ps1 should define $s3KeyPattern");
        Assert.Equal("^[A-Za-z0-9]{16,}$", match.Groups[1].Value);
        Assert.Contains("-notmatch $s3KeyPattern", script);
        Assert.Contains("Write-Warning", script);

        // PowerShell -match używa wyrażeń .NET bez rozróżniania wielkości liter
        var pattern = new Regex(match.Groups[1].Value, RegexOptions.IgnoreCase);
        Assert.Matches(pattern, "AbCdEfGh12345678");
        Assert.Matches(pattern, new string('x', 40));
        foreach (var invalid in new[] { "short1234", "with space 123456", "quote\"injection12345", "ąćęłńóśźż12345678", "abc-def-ghi-jkl-mno" })
        {
            Assert.DoesNotMatch(pattern, invalid);
        }
    }

    [Fact]
    public void Script_IsSavedWithUtf8Bom()
    {
        // Windows PowerShell 5.1 bez BOM czyta plik w kodowaniu ANSI i psuje polskie znaki
        var bytes = File.ReadAllBytes(RepositoryFiles.Find("docker-up.ps1"));

        Assert.True(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }
}
