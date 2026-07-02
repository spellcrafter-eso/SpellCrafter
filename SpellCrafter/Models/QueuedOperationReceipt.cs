using System;

namespace SpellCrafter.Models;

public sealed record QueuedOperationReceipt(
    Guid OperationId,
    int AddonCommonId,
    string AddonName,
    string OperationType,
    QueueOperationStatus Status,
    bool CancelRequested)
{
    public static QueuedOperationReceipt FromOperation(QueuedOperation operation)
    {
        return new QueuedOperationReceipt(
            operation.OperationId,
            operation.AddonCommonId,
            operation.AddonName,
            operation.OperationType,
            operation.Status,
            operation.CancelRequested);
    }
}
