using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Data;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

public sealed class SqliteAddonOperationLeaseStore : IAddonOperationLeaseStore
{
    private readonly IEsoDataConnectionFactory _dbConnectionFactory;
    private readonly object _sync = new();

    public SqliteAddonOperationLeaseStore(IEsoDataConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory
                               ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        EnsureTableExists();
    }

    public Task<OperationExecutorLease?> GetLeaseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var db = _dbConnectionFactory.CreateConnection();
        var entity = db.Find<OperationExecutorLeaseEntity>(IAddonOperationLeaseStore.DefaultLeaseName);
        return Task.FromResult(entity?.ToLease());
    }

    public Task<OperationExecutorLeaseHandle?> TryAcquireAsync(
        string ownerKind,
        string ownerId,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKind))
            throw new ArgumentException("Owner kind is required.", nameof(ownerKind));

        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("Owner id is required.", nameof(ownerId));

        if (timeToLive <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeToLive), "Lease time-to-live must be positive.");

        cancellationToken.ThrowIfCancellationRequested();

        var acquired = false;
        var now = DateTime.UtcNow;

        lock (_sync)
        {
            using var db = _dbConnectionFactory.CreateConnection();
            db.RunInTransaction(() =>
            {
                var existing = db.Find<OperationExecutorLeaseEntity>(IAddonOperationLeaseStore.DefaultLeaseName);

                if (existing != null && existing.ExpiresAtUtc > now)
                    return;

                var entity = new OperationExecutorLeaseEntity
                {
                    LeaseName = IAddonOperationLeaseStore.DefaultLeaseName,
                    OwnerId = ownerId,
                    OwnerKind = ownerKind,
                    ProcessId = Environment.ProcessId,
                    MachineName = Environment.MachineName,
                    AcquiredAtUtc = now,
                    LastHeartbeatUtc = now,
                    ExpiresAtUtc = now.Add(timeToLive)
                };

                if (existing == null)
                    db.Insert(entity);
                else
                    db.Update(entity);

                acquired = true;
            });
        }

        return Task.FromResult(acquired
            ? new OperationExecutorLeaseHandle(this, ownerId)
            : null);
    }

    public Task<bool> RenewAsync(
        string ownerId,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("Owner id is required.", nameof(ownerId));

        if (timeToLive <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeToLive), "Lease time-to-live must be positive.");

        cancellationToken.ThrowIfCancellationRequested();

        var renewed = false;
        var now = DateTime.UtcNow;

        lock (_sync)
        {
            using var db = _dbConnectionFactory.CreateConnection();
            db.RunInTransaction(() =>
            {
                var entity = db.Find<OperationExecutorLeaseEntity>(IAddonOperationLeaseStore.DefaultLeaseName);
                if (entity == null || entity.OwnerId != ownerId)
                    return;

                entity.LastHeartbeatUtc = now;
                entity.ExpiresAtUtc = now.Add(timeToLive);
                db.Update(entity);
                renewed = true;
            });
        }

        return Task.FromResult(renewed);
    }

    public Task<bool> ReleaseAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("Owner id is required.", nameof(ownerId));

        cancellationToken.ThrowIfCancellationRequested();

        var released = false;

        lock (_sync)
        {
            using var db = _dbConnectionFactory.CreateConnection();
            db.RunInTransaction(() =>
            {
                var entity = db.Find<OperationExecutorLeaseEntity>(IAddonOperationLeaseStore.DefaultLeaseName);
                if (entity == null || entity.OwnerId != ownerId)
                    return;

                db.Delete(entity);
                released = true;
            });
        }

        return Task.FromResult(released);
    }

    private void EnsureTableExists()
    {
        using var db = _dbConnectionFactory.CreateConnection();
        db.CreateTableIfNotExists<OperationExecutorLeaseEntity>();
        Debug.WriteLine("Operation executor lease table ensured.");
    }
}
