using Backend.Helpers;

namespace Backend.Tests.Unit;

// Blokada per klucz (logowanie per e-mail): ten sam klucz po kolei, różne równolegle, wpisy usuwane po użyciu
public class KeyedLockTests
{
    [Fact]
    public async Task SameKey_WaitsUntilTheHolderReleases()
    {
        var locks = new KeyedLock();
        var first = await locks.AcquireAsync("a");

        var second = locks.AcquireAsync("a");
        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        first.Dispose();
        using var _ = await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DifferentKeys_DoNotBlockEachOther()
    {
        var locks = new KeyedLock();
        using var a = await locks.AcquireAsync("a");

        using var b = await locks.AcquireAsync("b").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, locks.ActiveKeys);
    }

    [Fact]
    public async Task EntriesAreRemovedAfterUse_MemoryDoesNotGrowWithTheNumberOfKeys()
    {
        var locks = new KeyedLock();

        for (var i = 0; i < 1000; i++)
        {
            using var _ = await locks.AcquireAsync($"user{i}@test.local");
        }

        await Task.WhenAll(Enumerable.Range(0, 200).Select(async i =>
        {
            using var _ = await locks.AcquireAsync($"key{i % 7}");
            await Task.Yield();
        }));

        Assert.Equal(0, locks.ActiveKeys);
    }

    [Fact]
    public async Task ParallelHoldersOfOneKey_NeverOverlap()
    {
        var locks = new KeyedLock();
        var inside = 0;
        var maxInside = 0;

        await Task.WhenAll(Enumerable.Range(0, 50).Select(async _ =>
        {
            using var __ = await locks.AcquireAsync("same");
            var now = Interlocked.Increment(ref inside);
            InterlockedMax(ref maxInside, now);
            await Task.Delay(1);
            Interlocked.Decrement(ref inside);
        }));

        Assert.Equal(1, maxInside);
        Assert.Equal(0, locks.ActiveKeys);
    }

    [Fact]
    public async Task CancelledWait_LeavesTheLockUsable()
    {
        var locks = new KeyedLock();
        var holder = await locks.AcquireAsync("a");
        using var cts = new CancellationTokenSource();

        var waiting = locks.AcquireAsync("a", cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        holder.Dispose();
        holder.Dispose();
        Assert.Equal(0, locks.ActiveKeys);
        using var again = await locks.AcquireAsync("a").WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
