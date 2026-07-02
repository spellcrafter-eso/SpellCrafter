using System;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

/// <summary>
/// Runtime context for a queued operation.
/// Contains the operation itself plus non-persistable runtime state
/// (progress reporter, cancellation token, completion source).
/// </summary>
internal sealed class QueuedOperationExecutionContext
{
    public QueuedOperation Operation { get; init; } = null!;

    public IProgress<InstallProgress>? Progress { get; init; }

    public CancellationToken CancellationToken { get; init; }

    public TaskCompletionSource<InstallResult> Completion { get; init; } = null!;

    /// <summary>
    /// True once execution has started. Used to distinguish "cancel before execution"
    /// from "cancel during execution".
    /// </summary>
    public bool ExecutionStarted { get; set; }

    /// <summary>
    /// Internal cancellation source, triggered by <c>CancelOperationAsync</c>
    /// or other internal mechanisms. Combined with the user's CancellationToken
    /// via <c>CancellationTokenSource.CreateLinkedTokenSource</c>.
    /// </summary>
    public CancellationTokenSource InternalCts { get; init; } = new();
}