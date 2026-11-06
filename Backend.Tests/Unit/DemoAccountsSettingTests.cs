using Backend.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace Backend.Tests.Unit;

// Konta demo (znane hasła) tylko lokalnie: Development albo jawne Seed:DemoAccounts=true
public class DemoAccountsSettingTests
{
    private static bool Enabled(string environment, string? setting)
    {
        var values = new Dictionary<string, string?>();
        if (setting != null)
        {
            values["Seed:DemoAccounts"] = setting;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return DatabaseSeeder.DemoAccountsEnabled(new HostingEnvironment { EnvironmentName = environment }, configuration);
    }

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Production", null, false)]
    [InlineData("Staging", null, false)]
    [InlineData("Production", "true", true)]
    [InlineData("Production", "True", true)]
    [InlineData("Production", "false", false)]
    [InlineData("Development", "true", true)]
    // jawna wartość ma pierwszeństwo - także w Development można wyłączyć konta demo
    [InlineData("Development", "false", false)]
    public void DemoAccountsEnabled_DependsOnEnvironmentAndSetting(string environment, string? setting, bool expected)
    {
        Assert.Equal(expected, Enabled(environment, setting));
    }
}
