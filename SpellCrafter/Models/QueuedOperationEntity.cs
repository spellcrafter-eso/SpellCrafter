using System;
using SpellCrafter.Enums;
using SQLite;

namespace SpellCrafter.Models;

/// <summary>
/// SQLite entity for a queued addon operation.
/// </summary>
[Table("QueuedOperation")]
public sealed class QueuedOperationEntity
{
    [PrimaryKey] public string OperationId { get; set; } = string.Empty;

    public int AddonCommonId { get; set; }

    public string AddonName { get; set; } = string.Empty;

    public string OperationType { get; set; } = string.Empty;

    public int InstallationMethod { get; set; }

    public bool Recursive { get; set; }

    public int Priority { get; set; }

    /// <summary>Whether orphaned dependencies should be removed on delete.</summary>
    public bool RemoveOrphanedDependencies { get; set; }

    public int Status { get; set; }

    public DateTime RequestTime { get; set; }

    public DateTime? StartTime { get; set; }

    public DateTime? CompletionTime { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ResultMessage { get; set; }

    public bool CompletedAfterCancellation { get; set; }

    public bool CancelRequested { get; set; }

    public string? CancelReason { get; set; }

    public DateTime? CancelRequestedAtUtc { get; set; }

    /// <summary>
    /// Indicates that all worker/journal actions for this queue operation have
    /// completed successfully. Used for crash recovery: if true but status
    /// is InProgress, the operation can be safely marked Completed.
    /// </summary>
    public bool JournalCompleted { get; set; }

    public static QueuedOperationEntity FromQueuedOperation(QueuedOperation op)
    {
        return new QueuedOperationEntity
        {
            OperationId = op.OperationId.ToString("N"),
            AddonCommonId = op.AddonCommonId,
            AddonName = op.AddonName,
            OperationType = op.OperationType,
            InstallationMethod = (int)op.InstallationMethod,
            Recursive = op.Recursive,
            Priority = (int)op.Priority,
            RemoveOrphanedDependencies = op.DeleteOptions?.RemoveOrphanedDependencies ?? false,
            Status = (int)op.Status,
            RequestTime = op.RequestTime,
            StartTime = op.StartTime,
            CompletionTime = op.CompletionTime,
            ErrorMessage = op.ErrorMessage,
            ResultMessage = op.ResultMessage,
            CompletedAfterCancellation = op.CompletedAfterCancellation,
            CancelRequested = op.CancelRequested,
            CancelReason = op.CancelReason,
            CancelRequestedAtUtc = op.CancelRequestedAtUtc
        };
    }

    public QueuedOperation ToQueuedOperation()
    {
        Guid operationId;
        Guid.TryParseExact(OperationId, "N", out operationId);

        return new QueuedOperation
        {
            OperationId = operationId,
            AddonCommonId = AddonCommonId,
            AddonName = AddonName,
            OperationType = OperationType,
            InstallationMethod = (AddonInstallationMethod)InstallationMethod,
            Recursive = Recursive,
            Priority = (QueuePriority)Priority,
            DeleteOptions = RemoveOrphanedDependencies
                ? new DeleteOperationOptions(true)
                : null,
            Status = (QueueOperationStatus)Status,
            RequestTime = RequestTime,
            StartTime = StartTime,
            CompletionTime = CompletionTime,
            ErrorMessage = ErrorMessage,
            ResultMessage = ResultMessage,
            CompletedAfterCancellation = CompletedAfterCancellation,
            CancelRequested = CancelRequested,
            CancelReason = CancelReason,
            CancelRequestedAtUtc = CancelRequestedAtUtc
        };
    }
}
