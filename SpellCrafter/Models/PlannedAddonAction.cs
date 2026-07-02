namespace SpellCrafter.Models;

/// <summary>
/// A single addon action within an operation plan.
/// </summary>
public sealed record PlannedAddonAction(
    int CommonAddonId,
    string AddonName,
    string OperationType,
    int ExecutionOrder,
    bool IsRoot);