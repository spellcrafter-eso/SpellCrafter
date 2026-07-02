using System.Collections.Generic;

namespace SpellCrafter.Models;

/// <summary>
/// An executable plan for one user operation.
/// Built from a graph snapshot at planning time.
/// </summary>
public sealed record AddonOperationPlan(
    string OperationId,
    AddonOperationRequest Request,
    IReadOnlyList<PlannedAddonAction> Actions,
    IReadOnlySet<AddonResourceKey> Resources,
    bool HasCycle,
    bool IsRejected,
    string? RejectionReason)
{
    /// <summary>
    /// Creates a rejected plan with a reason.
    /// </summary>
    public static AddonOperationPlan Rejected(
        string operationId,
        AddonOperationRequest request,
        string reason)
    {
        return new AddonOperationPlan(
            operationId, request,
            new List<PlannedAddonAction>(),
            new HashSet<AddonResourceKey>(),
            false, true, reason);
    }
}