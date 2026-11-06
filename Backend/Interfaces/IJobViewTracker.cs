namespace Backend.Interfaces;

// Zliczanie wyświetleń szczegółów oferty: jedno wyświetlenie na (oferta, odwiedzający, dzień UTC).
// Implementacja nigdy nie zgłasza wyjątku - błąd zliczania nie może psuć pobrania oferty.
public interface IJobViewTracker
{
    // Odwiedzający = zalogowany użytkownik (userId) albo anonim rozpoznawany po IP + User-Agent
    Task TrackAsync(int jobOfferId, string? userId, string? ipAddress, string? userAgent, CancellationToken cancellationToken);
}
