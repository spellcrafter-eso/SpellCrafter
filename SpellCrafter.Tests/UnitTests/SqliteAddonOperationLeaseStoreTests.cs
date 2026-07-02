using SpellCrafter.Data;
using SpellCrafter.Models;
using SpellCrafter.Services;
using SpellCrafter.Tests.TestInfrastructure;

namespace SpellCrafter.Tests.UnitTests;

public sealed class SqliteAddonOperationLeaseStoreTests : IDisposable
{
    private readonly IEsoDataConnectionFactory _dbFactory;

    public SqliteAddonOperationLeaseStoreTests()
    {
        _dbFactory = new FakeEsoDataConnectionFactory();
    }

    public void Dispose()
    {
        if (_dbFactory is IDisposable disposable)
            disposable.Dispose();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenNoLease_ReturnsHandleAndPersistsLease()
    {
        var store = new SqliteAddonOperationLeaseStore(_dbFactory);

        await using var handle = await store.TryAcquireAsync("cli", "owner-1", TimeSpan.FromMinutes(1));

        Assert.NotNull(handle);

        var lease = await store.GetLeaseAsync();
        Assert.NotNull(lease);
        Assert.Equal("owner-1", lease.OwnerId);
        Assert.Equal("cli", lease.OwnerKind);
        Assert.False(lease.IsExpired(DateTime.UtcNow));
    }

    [Fact]
    public async Task TryAcquireAsync_WhenLiveLeaseExists_ReturnsNull()
    {
        var store = new SqliteAddonOperationLeaseStore(_dbFactory);
        await using var first = await store.TryAcquireAsync("ui", "owner-1", TimeSpan.FromMinutes(1));
        Assert.NotNull(first);

        var second = await store.TryAcquireAsync("cli", "owner-2", TimeSpan.FromMinutes(1));

        Assert.Null(second);
    }

    [Fact]
    public async Task TryAcquireAsync_WhenExistingLeaseExpired_ReplacesLease()
    {
        var now = DateTime.UtcNow;
        using (var db = _dbFactory.CreateConnection())
        {
            db.Insert(new OperationExecutorLeaseEntity
            {
                LeaseName = IAddonOperationLeaseStore.DefaultLeaseName,
                OwnerId = "stale-owner",
                OwnerKind = "ui",
                ProcessId = 123,
                MachineName = "test-machine",
                AcquiredAtUtc = now.AddMinutes(-10),
                LastHeartbeatUtc = now.AddMinutes(-10),
                ExpiresAtUtc = now.AddMinutes(-5)
            });
        }

        var store = new SqliteAddonOperationLeaseStore(_dbFactory);

        await using var handle = await store.TryAcquireAsync("cli", "fresh-owner", TimeSpan.FromMinutes(1));

        Assert.NotNull(handle);

        var lease = await store.GetLeaseAsync();
        Assert.NotNull(lease);
        Assert.Equal("fresh-owner", lease.OwnerId);
        Assert.Equal("cli", lease.OwnerKind);
    }

    [Fact]
    public async Task ReleaseAsync_OnlyOwnerCanReleaseLease()
    {
        var store = new SqliteAddonOperationLeaseStore(_dbFactory);
        await using var handle = await store.TryAcquireAsync("ui", "owner-1", TimeSpan.FromMinutes(1));
        Assert.NotNull(handle);

        var wrongOwnerReleased = await store.ReleaseAsync("owner-2");
        var leaseAfterWrongOwner = await store.GetLeaseAsync();

        Assert.False(wrongOwnerReleased);
        Assert.NotNull(leaseAfterWrongOwner);

        var ownerReleased = await store.ReleaseAsync("owner-1");
        var leaseAfterOwner = await store.GetLeaseAsync();

        Assert.True(ownerReleased);
        Assert.Null(leaseAfterOwner);
    }

    [Fact]
    public async Task RenewAsync_ExtendsLeaseForCurrentOwnerOnly()
    {
        var store = new SqliteAddonOperationLeaseStore(_dbFactory);
        await using var handle = await store.TryAcquireAsync("ui", "owner-1", TimeSpan.FromMinutes(1));
        Assert.NotNull(handle);

        var beforeRenew = await store.GetLeaseAsync();
        Assert.NotNull(beforeRenew);

        var wrongOwnerRenewed = await store.RenewAsync("owner-2", TimeSpan.FromMinutes(5));
        var ownerRenewed = await store.RenewAsync("owner-1", TimeSpan.FromMinutes(5));
        var afterRenew = await store.GetLeaseAsync();

        Assert.False(wrongOwnerRenewed);
        Assert.True(ownerRenewed);
        Assert.NotNull(afterRenew);
        Assert.True(afterRenew.ExpiresAtUtc > beforeRenew.ExpiresAtUtc);
    }

    [Fact]
    public async Task LeaseHandleDispose_ReleasesLease()
    {
        var store = new SqliteAddonOperationLeaseStore(_dbFactory);
        var handle = await store.TryAcquireAsync("cli", "owner-1", TimeSpan.FromMinutes(1));
        Assert.NotNull(handle);

        await handle.DisposeAsync();

        var lease = await store.GetLeaseAsync();
        Assert.Null(lease);
    }
}
