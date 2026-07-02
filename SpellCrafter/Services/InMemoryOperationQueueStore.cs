using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

/// <summary>
/// In-memory implementation of <see cref="IAddonOperationQueueStore"/>.
/// Thread-safe. Used for testing and as a bridge until a persistent store is needed.
/// </summary>
public sealed class InMemoryOperationQueueStore : IAddonOperationQueueStore
{
    private readonly ConcurrentDictionary<Guid, QueuedOperation> _operations = new();
    private readonly ConcurrentDictionary<Guid, bool> _journalCompleted = new();
    private readonly object _claimLock = new();

    public Task EnqueueAsync(QueuedOperation operation, CancellationToken cancellationToken = default)
    {
        _operations.TryAdd(operation.OperationId, operation);
        return Task.CompletedTask;
    }

    public Task<QueuedOperation?> TryClaimNextAsync(CancellationToken cancellationToken = default)
    {
        lock (_claimLock)
        {
            var next = _operations.Values
                .Where(o => o.Status == QueueOperationStatus.Pending)
                .OrderBy(o => o.Priority) // Normal (0) before Low (1)
                .ThenBy(o => o.RequestTime)
                .FirstOrDefault();

            if (next != null)
            {
                next.Status = QueueOperationStatus.InProgress;
                next.StartTime ??= DateTime.UtcNow;
            }

            return Task.FromResult(next);
        }
    }

    public Task<IReadOnlyList<QueuedOperation>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = _operations.Values.ToList().AsReadOnly();
        return Task.FromResult<IReadOnlyList<QueuedOperation>>(list);
    }

    public Task UpdateStatusAsync(
        Guid operationId,
        QueueOperationStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (_operations.TryGetValue(operationId, out var operation))
        {
            operation.Status = status;
            operation.ErrorMessage = errorMessage;
        }

        return Task.CompletedTask;
    }

    public Task UpdateResultAsync(
        Guid operationId,
        string? resultMessage,
        bool completedAfterCancellation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_operations.TryGetValue(operationId, out var operation))
        {
            operation.ResultMessage = resultMessage;
            operation.CompletedAfterCancellation = completedAfterCancellation;
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        _operations.TryRemove(operationId, out _);
        _journalCompleted.TryRemove(operationId, out _);
        return Task.CompletedTask;
    }

    public Task<int> RemoveTerminalAsync(
        IReadOnlySet<QueueOperationStatus> statuses,
        DateTime? completedBeforeUtc = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var terminalStatuses = GetRequestedTerminalStatuses(statuses);
        if (terminalStatuses.Count == 0)
            return Task.FromResult(0);

        var removed = 0;

        foreach (var operation in _operations.Values.ToList())
        {
            if (!terminalStatuses.Contains(operation.Status))
                continue;

            if (completedBeforeUtc != null &&
                (operation.CompletionTime == null || operation.CompletionTime >= completedBeforeUtc))
                continue;

            if (_operations.TryRemove(operation.OperationId, out _))
            {
                _journalCompleted.TryRemove(operation.OperationId, out _);
                removed++;
            }
        }

        return Task.FromResult(removed);
    }

    public Task<bool> RequestCancellationAsync(
        Guid operationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_operations.TryGetValue(operationId, out var operation))
            return Task.FromResult(false);

        lock (_claimLock)
        {
            if (operation.Status is QueueOperationStatus.Completed or QueueOperationStatus.Failed or QueueOperationStatus.Canceled)
                return Task.FromResult(false);

            operation.CancelRequested = true;
            operation.CancelReason = reason;
            operation.CancelRequestedAtUtc = DateTime.UtcNow;

            if (operation.Status == QueueOperationStatus.Pending)
            {
                operation.Status = QueueOperationStatus.Canceled;
                operation.CompletionTime = DateTime.UtcNow;
                operation.ErrorMessage = reason;
            }

            return Task.FromResult(true);
        }
    }

    public Task MarkJournalCompletedAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        _journalCompleted[operationId] = true;
        return Task.CompletedTask;
    }

    private static HashSet<QueueOperationStatus> GetRequestedTerminalStatuses(
        IReadOnlySet<QueueOperationStatus> statuses)
    {
        var terminalStatuses = new HashSet<QueueOperationStatus>
        {
            QueueOperationStatus.Completed,
            QueueOperationStatus.Failed,
            QueueOperationStatus.Canceled
        };

        terminalStatuses.IntersectWith(statuses);
        return terminalStatuses;
    }
}
