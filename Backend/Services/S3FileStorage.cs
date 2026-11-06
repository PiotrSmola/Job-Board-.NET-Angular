using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Backend.Interfaces;
using Backend.Settings;

namespace Backend.Services;

// Magazyn zgodny z S3 (SeaweedFS w docker-compose): adresowanie path-style, bucket tworzony przy starcie
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly ILogger<S3FileStorage> _logger;

    public S3FileStorage(S3StorageSettings settings, ILogger<S3FileStorage> logger)
    {
        _bucket = settings.Bucket;
        _logger = logger;

        var config = new AmazonS3Config
        {
            ServiceURL = settings.ServiceUrl,
            ForcePathStyle = true,
            // region jest wymagany do podpisu SigV4; SeaweedFS go nie weryfikuje
            AuthenticationRegion = "us-east-1",
            // sumy kontrolne CRC (domyślne w nowych SDK) tylko tam, gdzie wymaga ich API -
            // nie wszystkie magazyny zgodne z S3 obsługują nagłówki trailing checksum
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };

        _client = new AmazonS3Client(new BasicAWSCredentials(settings.AccessKey, settings.SecretKey), config);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.PutBucketAsync(new PutBucketRequest { BucketName = _bucket }, cancellationToken);
            _logger.LogInformation("Created S3 bucket {Bucket}", _bucket);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            // bucket istnieje - nic do zrobienia
        }
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            // zwykły podpis SigV4 z sumą SHA-256 treści (strumień z formularza jest przewijalny) zamiast
            // aws-chunked - najszerzej wspierany wariant w magazynach zgodnych z S3
            UseChunkEncoding = false
        };

        await _client.PutObjectAsync(request, cancellationToken);
    }

    public async Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(_bucket, key, cancellationToken);
            // zwolnienie zwróconego strumienia (FileStreamResult robi to po wysłaniu) zwalnia całą odpowiedź
            // i połączenie HTTP; rozmiar z nagłówka Content-Length odpowiedzi magazynu
            return new StoredFile(new ResponseOwningStream(response), response.ContentLength >= 0 ? response.ContentLength : null);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        // S3 zwraca sukces także dla nieistniejącego obiektu
        await _client.DeleteObjectAsync(_bucket, key, cancellationToken);
    }

    public async Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = prefix };

        while (true)
        {
            var response = await _client.ListObjectsV2Async(request, cancellationToken);

            // pojedyncze DeleteObject zamiast DeleteObjects - to drugie wymaga sumy kontrolnej (Content-MD5 albo
            // CRC), którą magazyny zgodne z S3 obsługują różnie; reset danych demo usuwa niewiele plików
            // (najwyżej 10 załączników na aplikację)
            foreach (var s3Object in response.S3Objects ?? [])
            {
                await _client.DeleteObjectAsync(_bucket, s3Object.Key, cancellationToken);
            }

            if (response.IsTruncated != true || string.IsNullOrEmpty(response.NextContinuationToken))
            {
                return;
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
    }

    public void Dispose() => _client.Dispose();

    // Strumień tylko do odczytu treści obiektu, który przy zwolnieniu zwalnia też GetObjectResponse
    private sealed class ResponseOwningStream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
