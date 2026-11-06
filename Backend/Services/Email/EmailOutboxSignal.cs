namespace Backend.Services.Email;

// Budzenie procesora outboksu po zatwierdzeniu nowych wiadomości (bez czekania na kolejny cykl odpytywania).
// Kilka sygnałów przed obudzeniem procesora łączy się w jeden (semafor z maksimum 1).
public sealed class EmailOutboxSignal : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public void Notify()
    {
        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // procesor jest już obudzony
        }
        catch (ObjectDisposedException)
        {
            // aplikacja się zamyka
        }
    }

    // true - sygnał, false - upłynął czas
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _semaphore.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _semaphore.Dispose();
}
