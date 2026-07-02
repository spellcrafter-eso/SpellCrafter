using System;
using System.Collections.Generic;
using System.Linq;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

/// <summary>
/// Builds executable operation plans from user requests and graph snapshots.
/// </summary>
public static class AddonOperationPlanner
{
    /// <summary>
    /// Builds a plan for a single user request based on a graph snapshot.
    /// The <paramref name="addonNameResolver"/> maps CommonAddonId to addon display name.
    /// If null, IDs are used as fallback names.
    /// </summary>
    public static AddonOperationPlan BuildPlan(
        AddonOperationRequest request,
        AddonGraphSnapshot snapshot,
        Func<int, string>? addonNameResolver = null)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var nameResolver = addonNameResolver ?? (id => id.ToString());

        // For delete operations, use the dedicated delete planner
        if (request.OperationType == AddonOperationType.Delete)
        {
            var (actions, rejected, reason) = ValidateAndBuildDelete(request, snapshot, nameResolver);
            if (rejected)
                return AddonOperationPlan.Rejected(operationId, request, reason!);

            return new AddonOperationPlan(
                operationId, request, actions,
                ComputeResources(actions, nameResolver),
                snapshot.HasCycle, false, null);
        }

        // For install/update/reinstall
        var allActions = BuildInstallLikeActions(request, snapshot, nameResolver);

        return new AddonOperationPlan(
            operationId, request, allActions,
            ComputeResources(allActions, nameResolver),
            snapshot.HasCycle, false, null);
    }

    private static (List<PlannedAddonAction> Actions, bool Rejected, string? Reason) ValidateAndBuildDelete(
        AddonOperationRequest request,
        AddonGraphSnapshot snapshot,
        Func<int, string> nameResolver)
    {
        var rootId = request.RootCommonAddonId;
        var rootName = nameResolver(rootId);
        var deleteSet = new HashSet<int> { rootId };

        if (request.DeleteOptions?.RemoveOrphanedDependencies == true)
        {
            // Compute orphan candidates: dependencies of root that become orphaned
            var orphanCandidates = ComputeOrphanCandidates(rootId, snapshot, deleteSet);

            foreach (var orphan in orphanCandidates)
            {
                // Check if orphan is still needed by addons outside the delete set
                var externalDependents = GetExternalDependents(orphan, snapshot, deleteSet);
                if (externalDependents.Count == 0)
                    deleteSet.Add(orphan);
            }
        }

        // Validate: check reverse dependents of the root outside the delete set
        var rootExternalDependents = GetExternalDependents(rootId, snapshot, deleteSet);
        if (rootExternalDependents.Count > 0)
        {
            var names = string.Join(", ", rootExternalDependents.OrderBy(x => x));
            return ([], true, $"Cannot delete addon {rootName} because installed addon(s) depend on it: {names}");
        }

        // Also validate orphans: some might have external dependents
        var toRemove = new List<int>();
        foreach (var id in deleteSet)
        {
            var external = GetExternalDependents(id, snapshot, deleteSet);
            if (external.Count > 0)
                toRemove.Add(id);
        }

        foreach (var id in toRemove)
            deleteSet.Remove(id);

        // Build delete actions in topological order (dependents first)
        var actions = new List<PlannedAddonAction>();
        var visited = new HashSet<int>();
        var order = 0;

        foreach (var id in TopologicalSortDelete(deleteSet, snapshot))
            if (visited.Add(id))
                actions.Add(new PlannedAddonAction(
                    id,
                    nameResolver(id),
                    AddonOperationType.Delete,
                    order++,
                    id == rootId));

        return (actions, false, null);
    }

    private static List<PlannedAddonAction> BuildInstallLikeActions(
        AddonOperationRequest request,
        AddonGraphSnapshot snapshot,
        Func<int, string> nameResolver)
    {
        var rootId = request.RootCommonAddonId;
        var rootName = nameResolver(rootId);
        var actions = new List<PlannedAddonAction>();

        if (!request.Recursive)
        {
            actions.Add(new PlannedAddonAction(
                rootId, rootName, request.OperationType, 0, true));
            return actions;
        }

        var closure = snapshot.GetClosureCombined(rootId);

        // Add dependency actions first (lower order = executed first)
        var order = 0;
        foreach (var depId in TopologicalSort(new HashSet<int>(closure), snapshot))
            actions.Add(new PlannedAddonAction(
                depId,
                nameResolver(depId),
                AddonOperationType.Install,
                order++,
                false));

        // Add root action last
        actions.Add(new PlannedAddonAction(
            rootId,
            rootName,
            request.OperationType,
            order,
            true));

        return actions;
    }

    private static IReadOnlySet<AddonResourceKey> ComputeResources(
        IReadOnlyList<PlannedAddonAction> actions,
        Func<int, string> nameResolver)
    {
        var resources = new HashSet<AddonResourceKey>();

        foreach (var action in actions)
        {
            resources.Add(AddonResourceKey.ForAddon(action.CommonAddonId));
            resources.Add(AddonResourceKey.ForFolder(action.AddonName));
        }

        return resources;
    }

    private static List<int> ComputeOrphanCandidates(
        int rootId,
        AddonGraphSnapshot snapshot,
        HashSet<int> deleteSet)
    {
        var closure = snapshot.GetClosureCombined(rootId);
        var candidates = new List<int>();

        foreach (var depId in closure)
            if (!deleteSet.Contains(depId))
                candidates.Add(depId);

        return candidates;
    }

    private static HashSet<int> GetExternalDependents(
        int addonId,
        AddonGraphSnapshot snapshot,
        HashSet<int> excludeSet)
    {
        var external = new HashSet<int>();

        if (snapshot.ReverseLocalDependencies.TryGetValue(addonId, out var dependents))
            foreach (var dep in dependents)
                if (!excludeSet.Contains(dep))
                    external.Add(dep);

        return external;
    }

    /// <summary>
    /// Topological sort for install order (dependencies first).
    /// </summary>
    private static List<int> TopologicalSort(
        HashSet<int> ids,
        AddonGraphSnapshot snapshot)
    {
        var sorted = new List<int>();
        var visited = new HashSet<int>();
        var visiting = new HashSet<int>();

        foreach (var id in ids.OrderBy(x => x)) DfsTopologicalSort(id, ids, snapshot.ForwardLocalDependencies, visited, visiting, sorted);

        return sorted;
    }

    /// <summary>
    /// Topological sort for delete order (dependents first, then dependencies).
    /// </summary>
    private static List<int> TopologicalSortDelete(
        HashSet<int> ids,
        AddonGraphSnapshot snapshot)
    {
        var sorted = new List<int>();
        var visited = new HashSet<int>();
        var visiting = new HashSet<int>();

        foreach (var id in ids.OrderBy(x => x))
            DfsTopologicalSortReverse(
                id, ids, snapshot.ReverseLocalDependencies,
                visited, visiting, sorted);

        sorted.Reverse(); // Dependents first
        return sorted;
    }

    private static void DfsTopologicalSort(
        int id,
        HashSet<int> inSet,
        IReadOnlyDictionary<int, IReadOnlySet<int>> edges,
        HashSet<int> visited,
        HashSet<int> visiting,
        List<int> sorted)
    {
        if (visited.Contains(id)) return;
        if (visiting.Contains(id)) return; // Cycle detected, skip
        visiting.Add(id);

        if (edges.TryGetValue(id, out var deps))
            foreach (var dep in deps.Where(d => inSet.Contains(d)))
                DfsTopologicalSort(dep, inSet, edges, visited, visiting, sorted);

        visiting.Remove(id);
        visited.Add(id);
        sorted.Add(id);
    }

    private static void DfsTopologicalSortReverse(
        int id,
        HashSet<int> inSet,
        IReadOnlyDictionary<int, IReadOnlySet<int>> edges,
        HashSet<int> visited,
        HashSet<int> visiting,
        List<int> sorted)
    {
        if (visited.Contains(id)) return;
        if (visiting.Contains(id)) return;
        visiting.Add(id);

        if (edges.TryGetValue(id, out var deps))
            foreach (var dep in deps.Where(d => inSet.Contains(d)))
                DfsTopologicalSortReverse(dep, inSet, edges, visited, visiting, sorted);

        visiting.Remove(id);
        visited.Add(id);
        sorted.Add(id);
    }
}