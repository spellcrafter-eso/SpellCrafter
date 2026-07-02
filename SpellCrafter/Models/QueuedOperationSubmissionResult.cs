namespace SpellCrafter.Models;

public sealed record QueuedOperationSubmissionResult(
    bool Accepted,
    QueuedOperationReceipt? Operation,
    string? RejectionReason)
{
    public static QueuedOperationSubmissionResult AcceptedOperation(QueuedOperation operation)
    {
        return new QueuedOperationSubmissionResult(
            true,
            QueuedOperationReceipt.FromOperation(operation),
            null);
    }

    public static QueuedOperationSubmissionResult Rejected(string reason)
    {
        return new QueuedOperationSubmissionResult(false, null, reason);
    }
}
