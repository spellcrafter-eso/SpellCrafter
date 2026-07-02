using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

/// <summary>
/// Coordinates queued addon operations. Accepts enqueue requests, processes
/// them with configurable concurrency, and notifies callers of completion.
/// </summary>
public interface IAddonOperationCoordinator
{
    /// <summary>
    /// Enqueues an install operation for the given addon.
    /// Returns a task that completes when the operation finishes.
    /// </summary>
    Task<InstallResult> EnqueueInstallAsync(
        Addon addon,
        AddonInstallationMethod installationMethod,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues an update operation.
    /// </summary>
    Task<InstallResult> EnqueueUpdateAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a reinstall operation.
    /// </summary>
    Task<InstallResult> EnqueueReinstallAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a delete operation with default options (root only, no orphan cleanup).
    /// </summary>
    Task<InstallResult> EnqueueDeleteAsync(
        Addon addon,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a delete operation with custom options.
    /// </summary>
    Task<InstallResult> EnqueueDeleteAsync(
        Addon addon,
        DeleteOperationOptions options,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Cancels a queued operation. If the operation is pending (not yet started),
    /// its status is set to Canceled directly. If it is in progress, its internal
    /// cancellation token is signaled and the operation will be aborted.
    /// </summary>
    Task CancelOperationAsync(Guid operationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all queued operations currently in the queue store.
    /// </summary>
    Task<IReadOnlyList<QueuedOperation>> GetAllQueuedOperationsAsync(CancellationToken cancellationToken = default);
}