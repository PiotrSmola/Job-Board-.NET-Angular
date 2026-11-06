using Backend.Interfaces;
using Backend.Services;
using Backend.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

// Start aplikacji zależy od konfiguracji magazynu plików (StorageSettings.Validate); każdy test na własnej fabryce
public class StorageStartupTests
{
    private static string AllMessages(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    [Fact]
    public async Task Startup_WithLocalProvider_CreatesStorageDirectory()
    {
        var factory = new JobBoardFactory();
        try
        {
            _ = factory.Server;

            Assert.True(Directory.Exists(factory.StoragePath));
            Assert.IsType<LocalFileStorage>(factory.Services.GetRequiredService<IFileStorage>());
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }

        Assert.False(Directory.Exists(factory.StoragePath));
    }

    [Theory]
    [InlineData(null, "key", "Storage:S3:ServiceUrl")]
    [InlineData("http://storage:8333", null, "Storage:S3:AccessKey")]
    public async Task Startup_WithIncompleteS3Configuration_Fails(string? serviceUrl, string? keys, string expectedMessage)
    {
        var factory = new JobBoardFactory()
            .WithSetting("Storage:Provider", "S3")
            .WithSetting("Storage:S3:ServiceUrl", serviceUrl)
            .WithSetting("Storage:S3:AccessKey", keys)
            .WithSetting("Storage:S3:SecretKey", keys);
        try
        {
            var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            Assert.Contains(expectedMessage, AllMessages(exception));
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }

    [Fact]
    public async Task Startup_WithUnknownProvider_Fails()
    {
        var factory = new JobBoardFactory().WithSetting("Storage:Provider", "Azure");
        try
        {
            var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            Assert.Contains("Storage:Provider must be 'Local' or 'S3'", AllMessages(exception));
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }
}
