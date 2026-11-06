using System.Security.Cryptography;
using System.Text;
using Backend.Data;
using Backend.Interfaces;
using Backend.Models;
using Backend.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Services;

// Wyświetlenia liczone przez dwa zapytania w jednej transakcji:
//  1. INSERT IGNORE do JobOfferViewers - wynik 0 wierszy oznacza, że odwiedzający już dziś widział ofertę;
//  2. INSERT ... ON DUPLICATE KEY UPDATE w JobOfferDailyViews - atomowe zwiększenie dziennego licznika.
// Surowe IP nie jest przechowywane: odwiedzający to skrót HMAC-SHA256 (16 bajtów) z kluczem pochodnym
// sekretu JWT (HKDF z osobnym parametrem "info"), więc skrótu nie da się odtworzyć bez sekretu.
public class JobViewTracker : IJobViewTracker
{
    private const string KeyDerivationInfo = "jobboard:job-view-visitor:v1";
    private const int MaxUserAgentLength = 512;

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobViewTracker> _logger;
    private readonly byte[] _hmacKey;

    public JobViewTracker(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        IOptions<JwtSettings> jwtSettings,
        ILogger<JobViewTracker> logger)
    {
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
        _hmacKey = DeriveKey(jwtSettings.Value.Secret);
    }

    public async Task TrackAsync(int jobOfferId, string? userId, string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        try
        {
            var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
            var visitor = HashVisitor(userId, ipAddress, userAgent);

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            var isNewVisitor = await _context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT IGNORE INTO `JobOfferViewers` (`JobOfferId`, `Date`, `VisitorHash`) VALUES ({jobOfferId}, {today}, {visitor})",
                cancellationToken) == 1;

            if (isNewVisitor)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO `JobOfferDailyViews` (`JobOfferId`, `Date`, `Views`) VALUES ({jobOfferId}, {today}, 1) ON DUPLICATE KEY UPDATE `Views` = `Views` + 1",
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // zliczanie jest dodatkiem - błąd (także przerwane żądanie) tylko logujemy
            _logger.LogWarning(ex, "Failed to count a view of job offer {JobOfferId}", jobOfferId);
        }
    }

    private byte[] HashVisitor(string? userId, string? ipAddress, string? userAgent)
    {
        var agent = userAgent is { Length: > MaxUserAgentLength } ? userAgent[..MaxUserAgentLength] : userAgent;
        // przedrostki rozdzielają przestrzenie użytkowników i anonimów (id użytkownika nie zderzy się z "IP|UA")
        var identity = string.IsNullOrEmpty(userId) ? $"a:{ipAddress}|{agent}" : $"u:{userId}";

        return HMACSHA256.HashData(_hmacKey, Encoding.UTF8.GetBytes(identity))
            .AsSpan(0, JobOfferViewer.VisitorHashLength).ToArray();
    }

    private static byte[] DeriveKey(string secret) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32,
            info: Encoding.UTF8.GetBytes(KeyDerivationInfo));
}
