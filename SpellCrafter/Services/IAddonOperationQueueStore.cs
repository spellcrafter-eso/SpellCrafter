using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

/// <summary>
/// Persistent store for queued addon operations.
/// </summary>
public interface IAddonOperationQueueStore
{
    /// <summary>Enqueues a new operation.</summary>
    Task EnqueueAsync(QueuedOperation operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims the next pending operation ordered by request time.
    /// Sets its status to <see cref="QueueOperationStatus.InProgress"/>.
    /// Returns the claimed operation, or null if no pending operation exists.
    /// </summary>
    Task<QueuedOperation?> TryClaimNextAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all queued operations.</summary>
    Task<IReadOnlyList<QueuedOperation>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates the status of an operation.</summary>
    Task UpdateStatusAsync(
        Guid operationId,
        QueueOperationStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>Updates non-error result metadata for an operation.</summary>
    Task UpdateResultAsync(
        Guid operationId,
        string? resultMessage,
        bool completedAfterCancellation,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a completed operation from the store.</summary>
    Task RemoveAsync(Guid operationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes terminal operations matching the requested terminal statuses.
    /// Pending and InProgress operations are never removed by this method.
    /// </summary>
    Task<int> RemoveTerminalAsync(
        IReadOnlySet<QueueOperationStatus> statuses,
        DateTime? completedBeforeUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a cancellation request. Pending operations transition directly to Canceled;
    /// InProgress operations keep running until their executor observes cancellation.
    /// Returns false when the operation does not exist or is already terminal.
    /// </summary>
    Task<bool> RequestCancellationAsync(
        Guid operationId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks that all journal/worker actions for the given operation have completed successfully,
    /// without yet transitioning the queue status to Completed. Used as the first phase of a
    /// two-phase commit: JournalCompleted=true before Status=Completed. On crash recovery, if
    /// JournalCompleted is true but Status is still InProgress, the operation can be safely
    /// transitioned to Completed without retrying.
    /// </summary>
    Task MarkJournalCompletedAsync(Guid operationId, CancellationToken cancellationToken = default);
}
