using System.Threading.RateLimiting;

namespace Backend.Services;

// Limiter polityki "uploads" dla jednego użytkownika:
// - okno stałe: najwyżej permitLimit żądań w oknie liczonym od pierwszego żądania po wygaśnięciu poprzedniego okna;
//   odrzucenie podaje RetryAfter = czas do końca bieżącego okna (wbudowany FixedWindowRateLimiter podaje zawsze
//   pełną długość okna),
// - wspólny dla wszystkich użytkowników limit równoległych uploadów (shared, np. ConcurrencyLimiter bez kolejki) -
//   lease trzymany do końca żądania; odrzucenie przez ten limit nie zużywa limitu okna użytkownika.
// Bez kolejki: AcquireAsync działa jak AttemptAcquire. Nie jest właścicielem shared (singleton w DI).
public sealed class UploadRateLimiter : RateLimiter
{
    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private readonly RateLimiter? _shared;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly long _createdAt;

    private long? _windowStart;
    private int _used;
    private long _successfulLeases;
    private long _failedLeases;

    public UploadRateLimiter(int permitLimit, TimeSpan window, RateLimiter? shared, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(permitLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        _permitLimit = permitLimit;
        _window = window;
        _shared = shared;
        _time = time;
        _createdAt = time.GetTimestamp();
    }

    // Bezczynny = bieżące okno wygasło (licznik i tak by się wyzerował) - PartitionedRateLimiter usuwa wtedy limiter
    // nieaktywnego użytkownika bez utraty stanu
    public override TimeSpan? IdleDuration
    {
        get
        {
            lock (_lock)
            {
                if (_windowStart is not { } start)
                {
                    return _time.GetElapsedTime(_createdAt);
                }

                var sinceStart = _time.GetElapsedTime(start);
                return sinceStart >= _window ? sinceStart - _window : null;
            }
        }
    }

    public override RateLimiterStatistics? GetStatistics()
    {
        lock (_lock)
        {
            RefreshWindow();
            return new RateLimiterStatistics
            {
                CurrentAvailablePermits = _permitLimit - _used,
                CurrentQueuedCount = 0,
                TotalSuccessfulLeases = Interlocked.Read(ref _successfulLeases),
                TotalFailedLeases = Interlocked.Read(ref _failedLeases)
            };
        }
    }

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(permitCount, _permitLimit);

        if (permitCount == 0)
        {
            // sprawdzenie dostępności bez zajmowania limitów
            lock (_lock)
            {
                RefreshWindow();
                return _used < _permitLimit ? Lease.Acquired(null) : Rejected(RetryAfter());
            }
        }

        // najpierw limit równoległości - jego odrzucenie nie zużywa limitu okna
        var sharedLease = _shared?.AttemptAcquire();
        if (sharedLease is { IsAcquired: false })
        {
            Interlocked.Increment(ref _failedLeases);
            return sharedLease;
        }

        TimeSpan retryAfter;
        lock (_lock)
        {
            RefreshWindow();
            if (_used + permitCount <= _permitLimit)
            {
                _windowStart ??= _time.GetTimestamp();
                _used += permitCount;
                Interlocked.Increment(ref _successfulLeases);
                return Lease.Acquired(sharedLease);
            }

            retryAfter = RetryAfter();
        }

        sharedLease?.Dispose();
        Interlocked.Increment(ref _failedLeases);
        return Rejected(retryAfter);
    }

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AttemptAcquireCore(permitCount));

    // wygasłe okno - licznik od zera, nowe okno zaczyna się przy następnym przyznanym żądaniu
    private void RefreshWindow()
    {
        if (_windowStart is { } start && _time.GetElapsedTime(start) >= _window)
        {
            _windowStart = null;
            _used = 0;
        }
    }

    private TimeSpan RetryAfter()
    {
        if (_windowStart is not { } start)
        {
            return TimeSpan.Zero;
        }

        var remaining = _window - _time.GetElapsedTime(start);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static Lease Rejected(TimeSpan retryAfter) => new(false, null, retryAfter);

    private sealed class Lease(bool isAcquired, RateLimitLease? sharedLease, TimeSpan? retryAfter) : RateLimitLease
    {
        private RateLimitLease? _sharedLease = sharedLease;

        public static Lease Acquired(RateLimitLease? sharedLease) => new(true, sharedLease, null);

        public override bool IsAcquired => isAcquired;

        public override IEnumerable<string> MetadataNames =>
            retryAfter.HasValue ? [MetadataName.RetryAfter.Name] : [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter.HasValue && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = retryAfter.Value;
                return true;
            }

            metadata = null;
            return false;
        }

        // zwalnia miejsce w limicie równoległości (okno nie oddaje zużytych żądań)
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Exchange(ref _sharedLease, null)?.Dispose();
            }
        }
    }
}
