namespace SpellCrafter.Models;

/// <summary>
/// Options for delete operations.
/// </summary>
public sealed record DeleteOperationOptions(
    bool RemoveOrphanedDependencies);