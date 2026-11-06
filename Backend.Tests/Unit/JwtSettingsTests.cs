using Backend.Settings;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Backend.Tests.Unit;

public class JwtSettingsTests
{
    private sealed class FakeEnvironment : IHostEnvironment
    {
        public FakeEnvironment(string name) => EnvironmentName = name;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static readonly string ValidSecret = new('a', JwtSettings.MinSecretLength);

    [Fact]
    public void Defaults_MatchApplicationConfiguration()
    {
        var settings = new JwtSettings();

        Assert.Equal("JwtSettings", JwtSettings.SectionName);
        Assert.Equal(string.Empty, settings.Secret);
        Assert.Equal("JobBoardAPI", settings.Issuer);
        Assert.Equal("JobBoardClient", settings.Audience);
        Assert.Equal(120, settings.ExpirationInMinutes);
        Assert.False(settings.UsesDevelopmentSecret);
    }

    [Fact]
    public void DevelopmentSecret_SatisfiesMinimumLength()
    {
        Assert.True(JwtSettings.DevelopmentSecret.Length >= JwtSettings.MinSecretLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureValidSecret_InDevelopmentWithoutSecret_FallsBackToDevelopmentKey(string secret)
    {
        var settings = new JwtSettings { Secret = secret };

        settings.EnsureValidSecret(new FakeEnvironment("Development"));

        Assert.Equal(JwtSettings.DevelopmentSecret, settings.Secret);
        Assert.True(settings.UsesDevelopmentSecret);
    }

    [Fact]
    public void EnsureValidSecret_InDevelopmentWithConfiguredSecret_KeepsIt()
    {
        var settings = new JwtSettings { Secret = ValidSecret };

        settings.EnsureValidSecret(new FakeEnvironment("Development"));

        Assert.Equal(ValidSecret, settings.Secret);
        Assert.False(settings.UsesDevelopmentSecret);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void EnsureValidSecret_OutsideDevelopmentWithoutSecret_Throws(string environment)
    {
        var settings = new JwtSettings();

        var exception = Assert.Throws<InvalidOperationException>(() => settings.EnsureValidSecret(new FakeEnvironment(environment)));

        Assert.Contains("JwtSettings:Secret is not configured", exception.Message);
        Assert.Contains("JwtSettings__Secret", exception.Message);
    }

    [Fact]
    public void EnsureValidSecret_OutsideDevelopmentWithWhitespaceSecret_Throws()
    {
        var settings = new JwtSettings { Secret = "     " };

        Assert.Throws<InvalidOperationException>(() => settings.EnsureValidSecret(new FakeEnvironment("Production")));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void EnsureValidSecret_WithSecretShorterThanMinimum_Throws(string environment)
    {
        var settings = new JwtSettings { Secret = new string('x', JwtSettings.MinSecretLength - 1) };

        var exception = Assert.Throws<InvalidOperationException>(() => settings.EnsureValidSecret(new FakeEnvironment(environment)));

        Assert.Contains("at least 32 characters", exception.Message);
    }

    [Fact]
    public void EnsureValidSecret_WithExactlyMinimumLength_Passes()
    {
        var settings = new JwtSettings { Secret = ValidSecret };

        settings.EnsureValidSecret(new FakeEnvironment("Production"));

        Assert.Equal(ValidSecret, settings.Secret);
    }

    [Fact]
    public void EnsureValidSecret_EnvironmentNameComparisonIsCaseInsensitive()
    {
        var settings = new JwtSettings();

        settings.EnsureValidSecret(new FakeEnvironment("development"));

        Assert.True(settings.UsesDevelopmentSecret);
    }
}
