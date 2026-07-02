using SpellCrafter.Models;

namespace SpellCrafter.Services;

/// <summary>
/// Creates immutable snapshots of the addon dependency graph.
/// </summary>
public interface IAddonDependencyGraphStore
{
    /// <summary>
    /// Builds a snapshot of the current dependency graph from the database.
    /// </summary>
    AddonGraphSnapshot CreateSnapshot();
}