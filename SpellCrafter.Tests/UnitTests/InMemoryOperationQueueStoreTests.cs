using System.Linq;
using SpellCrafter.Models;
using SpellCrafter.Services;

namespace SpellCrafter.Tests.UnitTests;

public sealed class InMemoryOperationQueueStoreTests
{
    private static InMemoryOperationQueueStore CreateStore()
    {
        return new InMemoryOperationQueueStore();
    }

    private static QueuedOperation CreateOperation(
        string operationType = "install",
        QueueOperationStatus status = QueueOperationStatus.Pending,
        int commonAddonId = 100)
    {
        return new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = commonAddonId,
            AddonName = "TestAddon",
            OperationType = operationType,
            Status = status,
            RequestTime = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task Enqueue_AddsOperation()
    {
        var store = CreateStore();
        var op = CreateOperation();

        await store.EnqueueAsync(op);

        var all = await store.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(op.OperationId, all[0].OperationId);
    }

    [Fact]
    public async Task TryClaimNext_ReturnsFirstPendingAndSetsInProgress()
    {
        var store = CreateStore();
        var op1 = CreateOperation(commonAddonId: 100);
        var op2 = CreateOperation(commonAddonId: 200);
        var op3 = CreateOperation(commonAddonId: 300);

        await store.EnqueueAsync(op1);
        await store.EnqueueAsync(op2);
        await store.EnqueueAsync(op3);

        // Mark op2 as completed
        await store.UpdateStatusAsync(op2.OperationId, QueueOperationStatus.Completed);

        var next = await store.TryClaimNextAsync();

        Assert.NotNull(next);
        Assert.Equal(op1.OperationId, next!.OperationId);
        Assert.Equal(QueueOperationStatus.InProgress, next.Status);
    }

    [Fact]
    public async Task TryClaimNext_ReturnsNullWhenNonePending()
    {
        var store = CreateStore();
        var op = CreateOperation(status: QueueOperationStatus.Completed);

        await store.EnqueueAsync(op);

        var next = await store.TryClaimNextAsync();
        Assert.Null(next);
    }

    [Fact]
    public async Task TryClaimNext_ReturnsNullWhenEmpty()
    {
        var store = CreateStore();
        var next = await store.TryClaimNextAsync();
        Assert.Null(next);
    }

    [Fact]
    public async Task TryClaimNext_IsAtomic_DoesNotClaimSameOperationTwice()
    {
        var store = CreateStore();
        var op = CreateOperation();

        await store.EnqueueAsync(op);

        var first = await store.TryClaimNextAsync();
        Assert.NotNull(first);

        var second = await store.TryClaimNextAsync();
        Assert.Null(second);
    }

    [Fact]
    public async Task UpdateStatus_ChangesStatusAndErrorMessage()
    {
        var store = CreateStore();
        var op = CreateOperation();
        await store.EnqueueAsync(op);

        await store.UpdateStatusAsync(op.OperationId, QueueOperationStatus.Failed, "Something went wrong");

        var all = await store.GetAllAsync();
        var updated = all.Single();
        Assert.Equal(QueueOperationStatus.Failed, updated.Status);
        Assert.Equal("Something went wrong", updated.ErrorMessage);
    }

    [Fact]
    public async Task UpdateStatus_DoesNotThrowForUnknownId()
    {
        var store = CreateStore();
        await store.UpdateStatusAsync(Guid.NewGuid(), QueueOperationStatus.Completed);
        // No exception expected
    }

    [Fact]
    public async Task Remove_RemovesOperation()
    {
        var store = CreateStore();
        var op = CreateOperation();
        await store.EnqueueAsync(op);

        await store.RemoveAsync(op.OperationId);

        var all = await store.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task GetAll_ReturnsAllOperations()
    {
        var store = CreateStore();
        var op1 = CreateOperation();
        var op2 = CreateOperation();
        var op3 = CreateOperation();

        await store.EnqueueAsync(op1);
        await store.EnqueueAsync(op2);
        await store.EnqueueAsync(op3);

        var all = await store.GetAllAsync();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task GetAll_ReturnsEmptyWhenNoOperations()
    {
        var store = CreateStore();
        var all = await store.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task UpdateResultAsync_UpdatesResultMetadata()
    {
        var store = CreateStore();
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
    public async Task RemoveTerminalAsync_RemovesOnlyRequestedTerminalStatuses()
    {
        var store = CreateStore();
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
        var store = CreateStore();
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
        var store = CreateStore();
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
        var store = CreateStore();
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
        var store = CreateStore();
        var op = CreateOperation();

        await store.EnqueueAsync(op);
        await store.UpdateStatusAsync(op.OperationId, QueueOperationStatus.Completed);

        var requested = await store.RequestCancellationAsync(op.OperationId, "Too late.");

        Assert.False(requested);

        var all = await store.GetAllAsync();
        var completed = Assert.Single(all);
        Assert.Equal(QueueOperationStatus.Completed, completed.Status);
        Assert.False(completed.CancelRequested);
    }
}