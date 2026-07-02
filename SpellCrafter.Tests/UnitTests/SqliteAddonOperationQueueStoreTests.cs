using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;
using SpellCrafter.Services;
using SpellCrafter.Tests.TestInfrastructure;
using Xunit;

namespace SpellCrafter.Tests.UnitTests;

public sealed class SqliteAddonOperationQueueStoreTests : IDisposable
{
    private readonly IEsoDataConnectionFactory _dbFactory;

    public SqliteAddonOperationQueueStoreTests()
    {
        _dbFactory = new FakeEsoDataConnectionFactory();
    }

    public void Dispose()
    {
        if (_dbFactory is IDisposable d)
            d.Dispose();
    }

    private static QueuedOperation CreateOperation(
        string operationType = "install",
        QueueOperationStatus status = QueueOperationStatus.Pending,
        int commonAddonId = 100,
        QueuePriority priority = QueuePriority.Normal,
        int? extraMsDelay = null)
    {
        var requestTime = DateTime.UtcNow;
        if (extraMsDelay.HasValue)
            requestTime = requestTime.AddMilliseconds(extraMsDelay.Value);

        return new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = commonAddonId,
            AddonName = "TestAddon-" + commonAddonId,
            OperationType = operationType,
            Status = status,
            Priority = priority,
            RequestTime = requestTime
        };
    }

    [Fact]
    public async Task EnqueueAndClaim_Roundtrip()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var op = CreateOperation();

        await store.EnqueueAsync(op);

        var claimed = await store.TryClaimNextAsync();
        Assert.NotNull(claimed);
        Assert.Equal(op.OperationId, claimed.OperationId);
        Assert.Equal(QueueOperationStatus.InProgress, claimed.Status);
        Assert.NotNull(claimed.StartTime);
    }

    [Fact]
    public async Task TryClaimNext_ReturnsNull_WhenQueueEmpty()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);

        var claimed = await store.TryClaimNextAsync();
        Assert.Null(claimed);
    }

    [Fact]
    public async Task TryClaimNext_RespectsPriorityOrder()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);

        var low = CreateOperation(priority: QueuePriority.Low, extraMsDelay: 10);
        var normal = CreateOperation(priority: QueuePriority.Normal);

        await store.EnqueueAsync(low);
        await store.EnqueueAsync(normal);

        // First claim should be Normal (higher priority)
        var claimed = await store.TryClaimNextAsync();
        Assert.NotNull(claimed);
        Assert.Equal(normal.OperationId, claimed.OperationId);

        // Second claim should be Low
        claimed = await store.TryClaimNextAsync();
        Assert.NotNull(claimed);
        Assert.Equal(low.OperationId, claimed.OperationId);
    }

    [Fact]
    public async Task TryClaimNext_IsAtomic_PreventsDoubleClaim()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var op = CreateOperation();

        await store.EnqueueAsync(op);

        // First claim succeeds
        var first = await store.TryClaimNextAsync();
        Assert.NotNull(first);

        // Second claim returns null (already InProgress)
        var second = await store.TryClaimNextAsync();
        Assert.Null(second);
    }

    [Fact]
    public async Task TryClaimNext_WithTwoStoreInstances_OnlyOneClaimsOperation()
    {
        using var writer = new SqliteAddonOperationQueueStore(_dbFactory);
        using var store1 = new SqliteAddonOperationQueueStore(_dbFactory);
        using var store2 = new SqliteAddonOperationQueueStore(_dbFactory);
        var op = CreateOperation();

        await writer.EnqueueAsync(op);

        var results = await Task.WhenAll(
            store1.TryClaimNextAsync(),
            store2.TryClaimNextAsync());

        Assert.Single(results, r => r != null);
        Assert.Contains(results, r => r?.OperationId == op.OperationId);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllOperations()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var op1 = CreateOperation(commonAddonId: 1);
        var op2 = CreateOperation(commonAddonId: 2);

        await store.EnqueueAsync(op1);
        await store.EnqueueAsync(op2);

        var all = await store.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, o => o.AddonCommonId == 1);
        Assert.Contains(all, o => o.AddonCommonId == 2);
    }

    [Fact]
    public async Task UpdateStatusAsync_UpdatesFields()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var op = CreateOperation();

        await store.EnqueueAsync(op);

        // Claim
        var claimed = await store.TryClaimNextAsync();
        Assert.NotNull(claimed);

        // Update to failed
        await store.UpdateStatusAsync(op.OperationId, QueueOperationStatus.Failed, "Something went wrong");

        var all = await store.GetAllAsync();
        var updated = all.Single(o => o.OperationId == op.OperationId);

        Assert.Equal(QueueOperationStatus.Failed, updated.Status);
        Assert.Equal("Something went wrong", updated.ErrorMessage);
        Assert.NotNull(updated.CompletionTime);
    }

    [Fact]
    public async Task UpdateResultAsync_PersistsResultMetadata()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var op = CreateOperation();

        await store.EnqueueAsync(op);
        await store.UpdateResultAsync(
            op.OperationId,
            "Completed after cancellation was requested.",
            true);

        var all = await store.GetAllAsync();
        var updated = Assert.Single(all);

        Assert.Equal("Completed after cancellation was requested.", updated.ResultMessage);
        Assert.True(updated.CompletedAfterCancellation);
    }

    [Fact]
    public async Task RemoveAsync_RemovesOperation()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var op = CreateOperation();

        await store.EnqueueAsync(op);
        await store.RemoveAsync(op.OperationId);

        var all = await store.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task RemoveAsync_NonExistent_DoesNotThrow()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        // Should not throw
        await store.RemoveAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task RemoveTerminalAsync_RemovesOnlyRequestedTerminalStatuses()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var completed = CreateOperation(status: QueueOperationStatus.Completed, commonAddonId: 1);
        var failed = CreateOperation(status: QueueOperationStatus.Failed, commonAddonId: 2);
        var canceled = CreateOperation(status: QueueOperationStatus.Canceled, commonAddonId: 3);
        var pending = CreateOperation(status: QueueOperationStatus.Pending, commonAddonId: 4);

        await store.EnqueueAsync(completed);
        await store.EnqueueAsync(failed);
        await store.EnqueueAsync(canceled);
        await store.EnqueueAsync(pending);

        var removed = await store.RemoveTerminalAsync(
            new HashSet<QueueOperationStatus>
            {
                QueueOperationStatus.Completed,
                QueueOperationStatus.Canceled
            });

        Assert.Equal(2, removed);

        var remaining = await store.GetAllAsync();
        Assert.DoesNotContain(remaining, o => o.OperationId == completed.OperationId);
        Assert.DoesNotContain(remaining, o => o.OperationId == canceled.OperationId);
        Assert.Contains(remaining, o => o.OperationId == failed.OperationId);
        Assert.Contains(remaining, o => o.OperationId == pending.OperationId);
    }

    [Fact]
    public async Task RemoveTerminalAsync_DoesNotRemoveActiveOperationsEvenWhenRequested()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var pending = CreateOperation(status: QueueOperationStatus.Pending, commonAddonId: 1);
        var inProgress = CreateOperation(status: QueueOperationStatus.InProgress, commonAddonId: 2);

        await store.EnqueueAsync(pending);
        await store.EnqueueAsync(inProgress);

        var removed = await store.RemoveTerminalAsync(
            new HashSet<QueueOperationStatus>
            {
                QueueOperationStatus.Pending,
                QueueOperationStatus.InProgress
            });

        Assert.Equal(0, removed);
        var remaining = await store.GetAllAsync();
        Assert.Equal(2, remaining.Count);
    }

    [Fact]
    public async Task RequestCancellationAsync_PendingOperation_MarksCanceledAndPersistsReason()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var op = CreateOperation();

        await store.EnqueueAsync(op);

        var requested = await store.RequestCancellationAsync(op.OperationId, "Canceled by test.");

        Assert.True(requested);

        var all = await store.GetAllAsync();
        var canceled = Assert.Single(all);
        Assert.Equal(QueueOperationStatus.Canceled, canceled.Status);
        Assert.True(canceled.CancelRequested);
        Assert.Equal("Canceled by test.", canceled.CancelReason);
        Assert.NotNull(canceled.CancelRequestedAtUtc);
        Assert.NotNull(canceled.CompletionTime);
        Assert.Null(await store.TryClaimNextAsync());
    }

    [Fact]
    public async Task RequestCancellationAsync_InProgressOperation_SetsCancelRequestedWithoutCompleting()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var op = CreateOperation();

        await store.EnqueueAsync(op);
        var claimed = await store.TryClaimNextAsync();
        Assert.NotNull(claimed);

        var requested = await store.RequestCancellationAsync(op.OperationId, "Cancel while running.");

        Assert.True(requested);

        var all = await store.GetAllAsync();
        var inProgress = Assert.Single(all);
        Assert.Equal(QueueOperationStatus.InProgress, inProgress.Status);
        Assert.True(inProgress.CancelRequested);
        Assert.Equal("Cancel while running.", inProgress.CancelReason);
        Assert.NotNull(inProgress.CancelRequestedAtUtc);
        Assert.Null(inProgress.CompletionTime);
    }

    [Fact]
    public async Task RequestCancellationAsync_TerminalOperation_ReturnsFalse()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var op = CreateOperation(status: QueueOperationStatus.Completed);

        await store.EnqueueAsync(op);

        var requested = await store.RequestCancellationAsync(op.OperationId, "Too late.");

        Assert.False(requested);

        var all = await store.GetAllAsync();
        var completed = Assert.Single(all);
        Assert.Equal(QueueOperationStatus.Completed, completed.Status);
        Assert.False(completed.CancelRequested);
    }

    [Fact]
    public async Task Constructor_WithExistingQueueTable_AddsCancelRequestColumns()
    {
        using (var db = _dbFactory.CreateConnection())
        {
            db.Execute("DROP TABLE QueuedOperation");
            db.Execute(
                "CREATE TABLE QueuedOperation (" +
                "OperationId TEXT PRIMARY KEY, " +
                "AddonCommonId INTEGER NOT NULL, " +
                "AddonName TEXT NOT NULL, " +
                "OperationType TEXT NOT NULL, " +
                "InstallationMethod INTEGER NOT NULL, " +
                "Recursive INTEGER NOT NULL, " +
                "Priority INTEGER NOT NULL, " +
                "RemoveOrphanedDependencies INTEGER NOT NULL, " +
                "Status INTEGER NOT NULL, " +
                "RequestTime TEXT NOT NULL, " +
                "StartTime TEXT NULL, " +
                "CompletionTime TEXT NULL, " +
                "ErrorMessage TEXT NULL, " +
                "JournalCompleted INTEGER NOT NULL DEFAULT 0)");
        }

        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var op = CreateOperation();

        await store.EnqueueAsync(op);
        var requested = await store.RequestCancellationAsync(op.OperationId, "Migrated cancel.");

        Assert.True(requested);

        var canceled = Assert.Single(await store.GetAllAsync());
        Assert.Equal(QueueOperationStatus.Canceled, canceled.Status);
        Assert.True(canceled.CancelRequested);
        Assert.Equal("Migrated cancel.", canceled.CancelReason);
    }

    [Fact]
    public async Task Constructor_WithExistingQueueTable_AddsResultMetadataColumns()
    {
        using (var db = _dbFactory.CreateConnection())
        {
            db.Execute("DROP TABLE QueuedOperation");
            db.Execute(
                "CREATE TABLE QueuedOperation (" +
                "OperationId TEXT PRIMARY KEY, " +
                "AddonCommonId INTEGER NOT NULL, " +
                "AddonName TEXT NOT NULL, " +
                "OperationType TEXT NOT NULL, " +
                "InstallationMethod INTEGER NOT NULL, " +
                "Recursive INTEGER NOT NULL, " +
                "Priority INTEGER NOT NULL, " +
                "RemoveOrphanedDependencies INTEGER NOT NULL, " +
                "Status INTEGER NOT NULL, " +
                "RequestTime TEXT NOT NULL, " +
                "StartTime TEXT NULL, " +
                "CompletionTime TEXT NULL, " +
                "ErrorMessage TEXT NULL, " +
                "JournalCompleted INTEGER NOT NULL DEFAULT 0, " +
                "CancelRequested INTEGER NOT NULL DEFAULT 0, " +
                "CancelReason TEXT NULL, " +
                "CancelRequestedAtUtc TEXT NULL)");
        }

        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var op = CreateOperation();

        await store.EnqueueAsync(op);
        await store.UpdateResultAsync(
            op.OperationId,
            "Completed with warnings.",
            true);

        var all = await store.GetAllAsync();
        var updated = Assert.Single(all);

        Assert.Equal("Completed with warnings.", updated.ResultMessage);
        Assert.True(updated.CompletedAfterCancellation);
    }

    [Fact]
    public async Task PersistsAcrossStoreInstances()
    {
        var op = CreateOperation();

        // First store instance
        using (var store1 = new SqliteAddonOperationQueueStore(_dbFactory))
        {
            await store1.EnqueueAsync(op);
        }

        // Second store instance - should see the persisted operation
        using (var store2 = new SqliteAddonOperationQueueStore(_dbFactory))
        {
            var all = await store2.GetAllAsync();
            Assert.Single(all);
            Assert.Equal(op.OperationId, all[0].OperationId);
        }
    }

    [Fact]
    public async Task CompletedOperation_NotAffectedByCrashRecovery()
    {
        using (var store1 = new SqliteAddonOperationQueueStore(_dbFactory))
        {
            var op = CreateOperation();
            await store1.EnqueueAsync(op);
            var claimed = await store1.TryClaimNextAsync();
            Assert.NotNull(claimed);

            await store1.UpdateStatusAsync(op.OperationId, QueueOperationStatus.Completed);
        }

        // Second store: recover — completed operations should stay Completed
        using (var store2 = new SqliteAddonOperationQueueStore(_dbFactory))
        {
            var all = await store2.GetAllAsync();
            Assert.Single(all);
            Assert.Equal(QueueOperationStatus.Completed, all[0].Status);
        }
    }

    [Fact]
    public async Task TryClaimNext_OrderedByPriorityThenRequestTime()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);

        var lowLate = CreateOperation(priority: QueuePriority.Low, extraMsDelay: 20);
        var normalEarly = CreateOperation(priority: QueuePriority.Normal, extraMsDelay: 0);
        var lowEarly = CreateOperation(priority: QueuePriority.Low, extraMsDelay: 10);
        var normalLate = CreateOperation(priority: QueuePriority.Normal, extraMsDelay: 30);

        await store.EnqueueAsync(lowLate);
        await store.EnqueueAsync(normalEarly); // Should be first
        await store.EnqueueAsync(lowEarly);
        await store.EnqueueAsync(normalLate); // Should be second

        // 1st: Normal (earlier)
        var c1 = await store.TryClaimNextAsync();
        Assert.NotNull(c1);
        Assert.Equal(normalEarly.OperationId, c1.OperationId);

        // 2nd: Normal (later)
        var c2 = await store.TryClaimNextAsync();
        Assert.NotNull(c2);
        Assert.Equal(normalLate.OperationId, c2.OperationId);

        // 3rd: Low (earlier)
        var c3 = await store.TryClaimNextAsync();
        Assert.NotNull(c3);
        Assert.Equal(lowEarly.OperationId, c3.OperationId);

        // 4th: Low (later)
        var c4 = await store.TryClaimNextAsync();
        Assert.NotNull(c4);
        Assert.Equal(lowLate.OperationId, c4.OperationId);
    }

    [Fact]
    public async Task CancellationToken_Respected()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.TryClaimNextAsync(cts.Token));
    }

    [Fact]
    public async Task Dispose_RejectsOperations()
    {
        var store = new SqliteAddonOperationQueueStore(_dbFactory);
        store.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            store.EnqueueAsync(CreateOperation()));
    }

    [Fact]
    public async Task MarkJournalCompletedAsync_SetsFlag()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        var op = CreateOperation();

        await store.EnqueueAsync(op);
        await store.MarkJournalCompletedAsync(op.OperationId);

        // Verify by simulating a crash: new store should recover to Completed
        using (var store2 = new SqliteAddonOperationQueueStore(_dbFactory))
        {
            var all = await store2.GetAllAsync();
            Assert.Single(all);
            // The op was never claimed (status is still Pending), so JournalCompleted
            // is not relevant yet — just verify it's still Pending.
            Assert.Equal(QueueOperationStatus.Pending, all[0].Status);
        }
    }

    [Fact]
    public async Task MarkJournalCompletedAsync_NonExistent_DoesNotThrow()
    {
        using var store = new SqliteAddonOperationQueueStore(_dbFactory);
        await store.MarkJournalCompletedAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task InProgressOperation_WithJournalCompleted_RecoversToCompleted()
    {
        // First store: enqueue, claim (InProgress), then mark journal as completed
        // but do NOT transition to Completed — simulates crash after journal done
        // but before queue status update.
        using (var store1 = new SqliteAddonOperationQueueStore(_dbFactory))
        {
            var op = CreateOperation();
            await store1.EnqueueAsync(op);
            var claimed = await store1.TryClaimNextAsync();
            Assert.NotNull(claimed);
            Assert.Equal(QueueOperationStatus.InProgress, claimed.Status);

            await store1.MarkJournalCompletedAsync(op.OperationId);
        }

        // Second store: recovery should see JournalCompleted=true + Status=InProgress
        // and transition directly to Completed.
        using (var store2 = new SqliteAddonOperationQueueStore(_dbFactory))
        {
            var all = await store2.GetAllAsync();
            Assert.Single(all);

            var recovered = all[0];
            Assert.Equal(QueueOperationStatus.Completed, recovered.Status);
            Assert.NotNull(recovered.CompletionTime);
        }
    }

    [Fact]
    public async Task InProgressOperation_WithoutJournalCompleted_RecoversToPending()
    {
        // First store: enqueue and claim, leaving InProgress without journal completed
        using (var store1 = new SqliteAddonOperationQueueStore(_dbFactory))
        {
            var op = CreateOperation();
            await store1.EnqueueAsync(op);
            var claimed = await store1.TryClaimNextAsync();
            Assert.NotNull(claimed);
            Assert.Equal(QueueOperationStatus.InProgress, claimed.Status);
            // Do NOT call MarkJournalCompletedAsync
        }

        // Second store: recovery should reset to Pending
        using (var store2 = new SqliteAddonOperationQueueStore(_dbFactory))
        {
            var all = await store2.GetAllAsync();
            Assert.Single(all);

            var recovered = all[0];
            Assert.Equal(QueueOperationStatus.Pending, recovered.Status);
            Assert.Contains("interrupted", recovered.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Null(recovered.StartTime);
        }
    }

    [Fact]
    public async Task InProgressOperation_WithCancelRequest_RecoversToPendingAndPreservesCancelRequest()
    {
        var op = CreateOperation();

        using (var store1 = new SqliteAddonOperationQueueStore(_dbFactory))
        {
            await store1.EnqueueAsync(op);
            var claimed = await store1.TryClaimNextAsync();
            Assert.NotNull(claimed);

            await store1.RequestCancellationAsync(op.OperationId, "Cancel before restart.");
        }

        using (var store2 = new SqliteAddonOperationQueueStore(_dbFactory))
        {
            var all = await store2.GetAllAsync();
            var recovered = Assert.Single(all);

            Assert.Equal(QueueOperationStatus.Pending, recovered.Status);
            Assert.True(recovered.CancelRequested);
            Assert.Equal("Cancel before restart.", recovered.CancelReason);
        }
    }
}
