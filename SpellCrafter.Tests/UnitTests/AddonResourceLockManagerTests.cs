using SpellCrafter.Models;
using SpellCrafter.Services;

namespace SpellCrafter.Tests.UnitTests;

public sealed class AddonResourceLockManagerTests
{
    [Fact]
    public async Task Acquire_SingleKey_ReturnsHandle()
    {
        var manager = new AddonResourceLockManager();
        var key = AddonResourceKey.ForAddon(100);

        using var handle = await manager.AcquireAsync(key);

        Assert.NotNull(handle);
    }

    [Fact]
    public async Task Acquire_SameKey_SerializesAccess()
    {
        var manager = new AddonResourceLockManager();
        var key = AddonResourceKey.ForAddon(100);

        // Acquire the lock on the current thread and hold it
        var handle1 = await manager.AcquireAsync(key);

        // Start a task that tries to acquire the same key
        var acquireTask2 = Task.Run(() => manager.AcquireAsync(key));

        // Give task2 a moment to try — it should block
        await Task.Delay(100);
        Assert.False(acquireTask2.IsCompleted,
            "Second acquire should block while first lock is held");

        // Release the first lock
        handle1.Dispose();

        // Now task2 should complete
        using var handle2 = await acquireTask2.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Acquire_DifferentKeys_ConcurrentAccess()
    {
        var manager = new AddonResourceLockManager();
        var key1 = AddonResourceKey.ForAddon(100);
        var key2 = AddonResourceKey.ForAddon(200);

        var acquired1 = false;
        var acquired2 = false;

        var task1 = Task.Run(async () =>
        {
            using var handle = await manager.AcquireAsync(key1);
            acquired1 = true;
            await Task.Delay(200);
        });

        var task2 = Task.Run(async () =>
        {
            using var handle = await manager.AcquireAsync(key2);
            acquired2 = true;
            await Task.Delay(200);
        });

        await Task.WhenAll(task1, task2);

        Assert.True(acquired1);
        Assert.True(acquired2);
    }

    [Fact]
    public async Task Acquire_MultipleKeys_OrderIndependent()
    {
        var manager = new AddonResourceLockManager();
        var keyA = AddonResourceKey.ForAddon(100);
        var keyB = AddonResourceKey.ForFolder("AddonB");

        // Acquire in one order
        using (var handle1 = await manager.AcquireAsync(new HashSet<AddonResourceKey> { keyA, keyB }))
        {
            // Should succeed
        }

        // Acquire in reverse order — should also succeed (locks released above)
        using (var handle2 = await manager.AcquireAsync(new HashSet<AddonResourceKey> { keyB, keyA }))
        {
            // Should succeed
        }
    }

    [Fact]
    public async Task Acquire_EmptySet_ReturnsNullDisposable()
    {
        var manager = new AddonResourceLockManager();

        using var handle = await manager.AcquireAsync(new HashSet<AddonResourceKey>());

        Assert.NotNull(handle);
    }

    [Fact]
    public async Task Acquire_Dispose_ReleasesLock()
    {
        var manager = new AddonResourceLockManager();
        var key = AddonResourceKey.ForAddon(100);

        var handle = await manager.AcquireAsync(key);
        handle.Dispose();

        // After dispose, should be able to re-acquire immediately
        using var handle2 = await manager.AcquireAsync(key);
    }

    [Fact]
    public async Task Acquire_Cancellation_Throws()
    {
        var manager = new AddonResourceLockManager();
        var key = AddonResourceKey.ForAddon(100);
        using var cts = new CancellationTokenSource();

        // Acquire the lock first
        using var handle = await manager.AcquireAsync(key);

        cts.Cancel();

        // Second acquire on same key should be canceled
        try
        {
            await manager.AcquireAsync(key, cts.Token);
            Assert.Fail("Expected OperationCanceledException but no exception was thrown.");
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }
}