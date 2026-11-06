using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Backend.Services;
using Backend.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.Tests.Integration;

// Test uruchamiany tylko z zewnętrznym magazynem S3 (np. SeaweedFS):
// JOBBOARD_TEST_S3_URL=http://jobboard-dev-seaweedfs:8333 (klucze opcjonalnie: JOBBOARD_TEST_S3_ACCESS_KEY / _SECRET_KEY)
public sealed class S3FactAttribute : FactAttribute
{
    public S3FactAttribute()
    {
        if (string.IsNullOrWhiteSpace(S3FileStorageTests.ServiceUrl))
        {
            Skip = $"Set {S3FileStorageTests.UrlVariable} (e.g. http://jobboard-dev-seaweedfs:8333) to run S3 storage tests";
        }
    }
}

// Każdy test na własnym buckecie, usuwanym (z zawartością) po teście
public sealed class S3FileStorageTests : IAsyncLifetime
{
    public const string UrlVariable = "JOBBOARD_TEST_S3_URL";

    public static string? ServiceUrl => Environment.GetEnvironmentVariable(UrlVariable);

    private readonly S3StorageSettings _settings = new()
    {
        ServiceUrl = ServiceUrl ?? string.Empty,
        Bucket = "jobboard-test-" + Guid.NewGuid().ToString("N")[..16],
        AccessKey = Environment.GetEnvironmentVariable("JOBBOARD_TEST_S3_ACCESS_KEY") ?? "test-access-key",
        SecretKey = Environment.GetEnvironmentVariable("JOBBOARD_TEST_S3_SECRET_KEY") ?? "test-secret-key"
    };

    private S3FileStorage? _storage;

    private S3FileStorage Storage => _storage ??= new S3FileStorage(_settings, NullLogger<S3FileStorage>.Instance);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_storage == null)
        {
            return;
        }

        _storage.Dispose();

        using var client = new AmazonS3Client(new BasicAWSCredentials(_settings.AccessKey, _settings.SecretKey),
            new AmazonS3Config { ServiceURL = _settings.ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        try
        {
            var objects = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _settings.Bucket });
            foreach (var s3Object in objects.S3Objects ?? [])
            {
                await client.DeleteObjectAsync(_settings.Bucket, s3Object.Key);
            }

            await client.DeleteBucketAsync(_settings.Bucket);
        }
        catch (AmazonS3Exception)
        {
            // bucket nie powstał
        }
    }

    private async Task SaveTextAsync(string key, string text)
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await Storage.SaveAsync(key, content, "application/pdf");
    }

    private async Task<string?> ReadTextAsync(string key)
    {
        await using var file = await Storage.OpenReadAsync(key);
        if (file == null) return null;
        using var reader = new StreamReader(file.Content);
        return await reader.ReadToEndAsync();
    }

    [S3Fact]
    public async Task Initialize_CreatesBucketAndIsIdempotent()
    {
        await Storage.InitializeAsync();
        await Storage.InitializeAsync();

        await SaveTextAsync("attachments/1/a.pdf", "content");
        Assert.Equal("content", await ReadTextAsync("attachments/1/a.pdf"));
    }

    [S3Fact]
    public async Task SaveOpenDelete_RoundTrip()
    {
        await Storage.InitializeAsync();

        await SaveTextAsync("attachments/1/a.pdf", "first");
        await SaveTextAsync("attachments/1/a.pdf", "second");
        Assert.Equal("second", await ReadTextAsync("attachments/1/a.pdf"));

        await Storage.DeleteAsync("attachments/1/a.pdf");

        Assert.Null(await ReadTextAsync("attachments/1/a.pdf"));
        await Storage.DeleteAsync("attachments/1/a.pdf");
    }

    [S3Fact]
    public async Task OpenRead_ReportsTheObjectLengthFromTheResponse()
    {
        await Storage.InitializeAsync();
        await SaveTextAsync("attachments/1/len.pdf", "123456789");

        await using var file = await Storage.OpenReadAsync("attachments/1/len.pdf");

        Assert.NotNull(file);
        Assert.Equal(9, file!.Length);
        // strumień odpowiedzi S3 nie jest przewijalny - rozmiar pochodzi z nagłówka Content-Length
        Assert.False(file.Content.CanSeek);
    }

    [S3Fact]
    public async Task Save_LargeFile_RoundTrip()
    {
        await Storage.InitializeAsync();
        var content = new byte[5 * 1024 * 1024];
        new Random(42).NextBytes(content);

        using (var input = new MemoryStream(content))
        {
            await Storage.SaveAsync("attachments/2/big.pdf", input, "application/pdf");
        }

        await using var file = await Storage.OpenReadAsync("attachments/2/big.pdf");
        Assert.NotNull(file);
        Assert.Equal(content.Length, file!.Length);
        using var copy = new MemoryStream();
        await file.Content.CopyToAsync(copy);
        Assert.Equal(content, copy.ToArray());
    }

    [S3Fact]
    public async Task ManyFilesOfOneApplication_CanBeSavedInParallelAndDeletedByPrefix()
    {
        await Storage.InitializeAsync();

        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => SaveTextAsync($"attachments/3/{i}.pdf", $"file {i}")));

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal($"file {i}", await ReadTextAsync($"attachments/3/{i}.pdf"));
        }

        await Storage.DeleteByPrefixAsync("attachments/");
        for (var i = 0; i < 10; i++)
        {
            Assert.Null(await ReadTextAsync($"attachments/3/{i}.pdf"));
        }
    }

    [S3Fact]
    public async Task DeleteByPrefix_RemovesOnlyMatchingObjects()
    {
        await Storage.InitializeAsync();
        await SaveTextAsync("attachments/1/a.pdf", "a");
        await SaveTextAsync("attachments/2/b.docx", "b");
        await SaveTextAsync("other/c.txt", "c");

        await Storage.DeleteByPrefixAsync("attachments/");

        Assert.Null(await ReadTextAsync("attachments/1/a.pdf"));
        Assert.Null(await ReadTextAsync("attachments/2/b.docx"));
        Assert.Equal("c", await ReadTextAsync("other/c.txt"));
    }
}
