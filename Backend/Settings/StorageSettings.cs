namespace Backend.Settings;

// Ustawienia magazynu plików (sekcja "Storage") - dysk lokalny (domyślnie) albo magazyn zgodny z S3 (SeaweedFS)
public class StorageSettings
{
    public const string SectionName = "Storage";
    public const string LocalProvider = "Local";
    public const string S3Provider = "S3";

    public string Provider { get; set; } = LocalProvider;

    // katalog plików dla Local; domyślnie <ContentRoot>/storage
    public string? LocalPath { get; set; }

    public S3StorageSettings S3 { get; set; } = new();

    public bool UsesS3 => string.Equals(Provider, S3Provider, StringComparison.OrdinalIgnoreCase);

    // Uzupełnia wartości domyślne i waliduje konfigurację; rzuca InvalidOperationException przy złej konfiguracji
    public void Validate(IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(Provider))
        {
            Provider = LocalProvider;
        }

        if (UsesS3)
        {
            Provider = S3Provider;
            S3.Validate();
            return;
        }

        if (!string.Equals(Provider, LocalProvider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Provider must be '{LocalProvider}' or '{S3Provider}' (got '{Provider}').");
        }

        Provider = LocalProvider;
        LocalPath = string.IsNullOrWhiteSpace(LocalPath)
            ? Path.Combine(environment.ContentRootPath, "storage")
            : Path.GetFullPath(LocalPath, environment.ContentRootPath);
    }
}

public class S3StorageSettings
{
    public const string DefaultBucket = "jobboard";

    public string ServiceUrl { get; set; } = string.Empty;
    public string Bucket { get; set; } = DefaultBucket;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    public void Validate()
    {
        const string prefix = StorageSettings.SectionName + ":S3";

        if (string.IsNullOrWhiteSpace(ServiceUrl)
            || !Uri.TryCreate(ServiceUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"{prefix}:ServiceUrl must be an absolute http(s) URL when {StorageSettings.SectionName}:Provider is S3 " +
                $"(e.g. {StorageSettings.SectionName}__S3__ServiceUrl=http://storage:8333).");
        }

        if (string.IsNullOrWhiteSpace(AccessKey) || string.IsNullOrWhiteSpace(SecretKey))
        {
            throw new InvalidOperationException(
                $"{prefix}:AccessKey and {prefix}:SecretKey are required when {StorageSettings.SectionName}:Provider is S3.");
        }

        if (string.IsNullOrWhiteSpace(Bucket))
        {
            Bucket = DefaultBucket;
        }
    }
}
