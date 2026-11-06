using Backend.Settings;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Backend.Tests.Unit;

// Konfiguracja magazynu plików (sekcja Storage) - wartości domyślne i czytelne błędy złej konfiguracji
public class StorageSettingsTests
{
    private static readonly string ContentRoot = Path.Combine(Path.GetTempPath(), "jobboard-content-root");

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Backend";
        public string ContentRootPath { get; set; } = ContentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static S3StorageSettings ValidS3() => new()
    {
        ServiceUrl = "http://storage:8333",
        AccessKey = "access",
        SecretKey = "secret"
    };

    [Fact]
    public void Defaults_UseLocalStorageInContentRoot()
    {
        var settings = new StorageSettings();

        settings.Validate(new TestEnvironment());

        Assert.Equal(StorageSettings.LocalProvider, settings.Provider);
        Assert.False(settings.UsesS3);
        Assert.Equal(Path.Combine(ContentRoot, "storage"), settings.LocalPath);
    }

    [Fact]
    public void RelativeLocalPath_IsResolvedAgainstContentRoot()
    {
        var settings = new StorageSettings { Provider = "local", LocalPath = "files" };

        settings.Validate(new TestEnvironment());

        Assert.Equal(StorageSettings.LocalProvider, settings.Provider);
        Assert.Equal(Path.Combine(ContentRoot, "files"), settings.LocalPath);
    }

    [Fact]
    public void S3_WithFullConfiguration_IsValidAndUsesDefaultBucket()
    {
        var settings = new StorageSettings { Provider = "s3", S3 = ValidS3() };

        settings.Validate(new TestEnvironment());

        Assert.True(settings.UsesS3);
        Assert.Equal(StorageSettings.S3Provider, settings.Provider);
        Assert.Equal(S3StorageSettings.DefaultBucket, settings.S3.Bucket);
    }

    [Theory]
    [InlineData("")]
    [InlineData("storage:8333")]
    [InlineData("ftp://storage:8333")]
    public void S3_WithoutValidServiceUrl_Throws(string serviceUrl)
    {
        var s3 = ValidS3();
        s3.ServiceUrl = serviceUrl;
        var settings = new StorageSettings { Provider = "S3", S3 = s3 };

        var exception = Assert.Throws<InvalidOperationException>(() => settings.Validate(new TestEnvironment()));
        Assert.Contains("Storage:S3:ServiceUrl", exception.Message);
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("access", " ")]
    public void S3_WithoutKeys_Throws(string accessKey, string secretKey)
    {
        var s3 = ValidS3();
        s3.AccessKey = accessKey;
        s3.SecretKey = secretKey;
        var settings = new StorageSettings { Provider = "S3", S3 = s3 };

        var exception = Assert.Throws<InvalidOperationException>(() => settings.Validate(new TestEnvironment()));
        Assert.Contains("Storage:S3:AccessKey", exception.Message);
    }

    [Fact]
    public void UnknownProvider_Throws()
    {
        var settings = new StorageSettings { Provider = "Azure" };

        var exception = Assert.Throws<InvalidOperationException>(() => settings.Validate(new TestEnvironment()));
        Assert.Contains("Storage:Provider", exception.Message);
    }
}
