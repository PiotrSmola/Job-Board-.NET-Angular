using System.Threading.RateLimiting;
using Backend.Services;

namespace Backend.Tests.Unit;

// Limiter partycji polityk "uploads" / "auth" / "public-lists": okno stałe z Retry-After = czas do końca okna
// i wspólny limit równoległych żądań, którego odrzucenie nie zużywa limitu okna
public class WindowRateLimiterTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(600);

    // czas sterowany ręcznie (znacznik czasu w tickach TimeSpan)
    private sealed class ManualTime : TimeProvider
    {
        private long _ticks = TimeSpan.FromHours(1).Ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    private static TimeSpan? RetryAfter(RateLimitLease lease) =>
        lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : null;

    [Fact]
    public void Rejection_ReportsTheTimeLeftUntilTheEndOfTheWindow()
    {
        var time = new ManualTime();
        using var limiter = new WindowRateLimiter(2, Window, null, time);

        Assert.True(limiter.AttemptAcquire().IsAcquired);
        time.Advance(TimeSpan.FromSeconds(100));
        Assert.True(limiter.AttemptAcquire().IsAcquired);
        time.Advance(TimeSpan.FromSeconds(150));

        using var rejected = limiter.AttemptAcquire();

        Assert.False(rejected.IsAcquired);
        // okno liczone od pierwszego żądania: 600 - 250 s
        Assert.Equal(TimeSpan.FromSeconds(350), RetryAfter(rejected));

        time.Advance(TimeSpan.FromSeconds(349));
        Assert.Equal(TimeSpan.FromSeconds(1), RetryAfter(limiter.AttemptAcquire()));
    }

    [Fact]
    public void AfterTheWindow_TheLimitStartsAgainFromTheNextRequest()
    {
        var time = new ManualTime();
        using var limiter = new WindowRateLimiter(1, Window, null, time);
        Assert.True(limiter.AttemptAcquire().IsAcquired);
        time.Advance(Window);

        Assert.True(limiter.AttemptAcquire().IsAcquired);
        time.Advance(TimeSpan.FromSeconds(10));
        var rejected = limiter.AttemptAcquire();

        Assert.False(rejected.IsAcquired);
        Assert.Equal(Window - TimeSpan.FromSeconds(10), RetryAfter(rejected));
    }

    [Fact]
    public async Task AcquireAsync_DoesNotQueue()
    {
        var time = new ManualTime();
        using var limiter = new WindowRateLimiter(1, Window, null, time);
        Assert.True((await limiter.AcquireAsync()).IsAcquired);

        var rejected = await limiter.AcquireAsync();

        Assert.False(rejected.IsAcquired);
        Assert.Equal(Window, RetryAfter(rejected));
    }

    [Fact]
    public void ConcurrencyLimit_IsSharedByAllUsersAndReleasedWithTheLease()
    {
        var time = new ManualTime();
        using var concurrency = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 });
        using var alice = new WindowRateLimiter(30, Window, concurrency, time);
        using var bob = new WindowRateLimiter(30, Window, concurrency, time);

        var running = alice.AttemptAcquire();
        Assert.True(running.IsAcquired);

        var rejected = bob.AttemptAcquire();
        Assert.False(rejected.IsAcquired);
        // odrzucenie przez limit równoległości nie zużywa okna użytkownika
        Assert.Equal(30, bob.GetStatistics()!.CurrentAvailablePermits);

        running.Dispose();
        using var next = bob.AttemptAcquire();
        Assert.True(next.IsAcquired);
        Assert.Equal(29, bob.GetStatistics()!.CurrentAvailablePermits);
    }

    [Fact]
    public void WindowRejection_GivesBackTheConcurrencyPermit()
    {
        var time = new ManualTime();
        using var concurrency = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 });
        using var alice = new WindowRateLimiter(1, Window, concurrency, time);
        using var bob = new WindowRateLimiter(1, Window, concurrency, time);
        alice.AttemptAcquire().Dispose();

        Assert.False(alice.AttemptAcquire().IsAcquired);

        using var lease = bob.AttemptAcquire();
        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public void DisposingTheLeaseTwice_ReleasesOnePermit()
    {
        var time = new ManualTime();
        using var concurrency = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 2, QueueLimit = 0 });
        using var limiter = new WindowRateLimiter(30, Window, concurrency, time);
        var first = limiter.AttemptAcquire();
        using var second = limiter.AttemptAcquire();

        first.Dispose();
        first.Dispose();

        Assert.Equal(1, concurrency.GetStatistics()!.CurrentAvailablePermits);
    }

    [Fact]
    public void IdleDuration_IsNullDuringTheWindowAndCountsFromItsEnd()
    {
        var time = new ManualTime();
        using var limiter = new WindowRateLimiter(5, Window, null, time);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(3), limiter.IdleDuration);

        limiter.AttemptAcquire().Dispose();
        time.Advance(TimeSpan.FromSeconds(599));
        Assert.Null(limiter.IdleDuration);

        time.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(TimeSpan.FromSeconds(5), limiter.IdleDuration);
    }

    [Fact]
    public void PermitCountAboveTheLimit_Throws()
    {
        using var limiter = new WindowRateLimiter(2, Window, null, new ManualTime());

        Assert.Throws<ArgumentOutOfRangeException>(() => limiter.AttemptAcquire(3));
    }
}
