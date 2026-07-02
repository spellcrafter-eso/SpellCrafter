using System;
using SpellCrafter.Enums;

namespace SpellCrafter.Models;

/// <summary>
/// Represents a single operation waiting to be executed by the operation coordinator.
/// </summary>
public sealed class QueuedOperation
{
    /// <summary>Unique identifier for this queued operation.</summary>
    public Guid OperationId { get; init; }

    /// <summary>CommonAddonId of the addon to operate on.</summary>
    public int AddonCommonId { get; init; }

    /// <summary>Addon name for display purposes.</summary>
    public string AddonName { get; init; } = string.Empty;

    /// <summary>Type of operation: install, update, reinstall, delete.</summary>
    public string OperationType { get; init; } = string.Empty;

    /// <summary>The installation method to use (for install/update/reinstall).</summary>
    public AddonInstallationMethod InstallationMethod { get; init; }

    /// <summary>Whether to also process dependencies recursively.</summary>
    public bool Recursive { get; init; }

    /// <summary>Priority level. Higher-priority operations execute first.</summary>
    public QueuePriority Priority { get; init; } = QueuePriority.Normal;

    /// <summary>Options for delete operations (optional, only used for Delete type).</summary>
    public DeleteOperationOptions? DeleteOptions { get; init; }

    /// <summary>Current status of this operation.</summary>
    public QueueOperationStatus Status { get; set; }

    /// <summary>When the operation was enqueued.</summary>
    public DateTime RequestTime { get; init; }

    /// <summary>When execution started.</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>When execution completed.</summary>
    public DateTime? CompletionTime { get; set; }

    /// <summary>Error message if the operation failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Non-error result or warning message for terminal operations.</summary>
    public string? ResultMessage { get; set; }

    /// <summary>Whether the operation completed after cancellation was requested.</summary>
    public bool CompletedAfterCancellation { get; set; }

    /// <summary>Whether cancellation has been requested for this operation.</summary>
    public bool CancelRequested { get; set; }

    /// <summary>Reason supplied when cancellation was requested.</summary>
    public string? CancelReason { get; set; }

    /// <summary>When cancellation was requested.</summary>
    public DateTime? CancelRequestedAtUtc { get; set; }
}
