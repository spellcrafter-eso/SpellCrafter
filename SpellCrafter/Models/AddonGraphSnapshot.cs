using System.Collections.Generic;
using System.Linq;

namespace SpellCrafter.Models;

/// <summary>
/// Immutable snapshot of addon dependency graph at a point in time.
/// Builds reverse maps from forward maps automatically.
/// </summary>
public sealed class AddonGraphSnapshot
{
    private readonly IReadOnlyDictionary<int, IReadOnlySet<int>> _forwardLocal;
    private readonly IReadOnlyDictionary<int, IReadOnlySet<int>> _forwardOnline;
    private readonly bool _hasCycle;

    private AddonGraphSnapshot(
        IReadOnlyDictionary<int, IReadOnlySet<int>> forwardLocal,
        IReadOnlyDictionary<int, IReadOnlySet<int>> reverseLocal,
        IReadOnlyDictionary<int, IReadOnlySet<int>> forwardOnline,
        IReadOnlyDictionary<int, IReadOnlySet<int>> reverseOnline,
        IReadOnlySet<int> allAddonIds,
        bool hasCycle)
    {
        _forwardLocal = forwardLocal;
        _forwardOnline = forwardOnline;
        ForwardLocalDependencies = forwardLocal;
        ReverseLocalDependencies = reverseLocal;
        ForwardOnlineDependencies = forwardOnline;
        ReverseOnlineDependencies = reverseOnline;
        AllAddonIds = allAddonIds;
        _hasCycle = hasCycle;
    }

    public IReadOnlyDictionary<int, IReadOnlySet<int>> ForwardLocalDependencies { get; }
    public IReadOnlyDictionary<int, IReadOnlySet<int>> ReverseLocalDependencies { get; }
    public IReadOnlyDictionary<int, IReadOnlySet<int>> ForwardOnlineDependencies { get; }
    public IReadOnlyDictionary<int, IReadOnlySet<int>> ReverseOnlineDependencies { get; }
    public IReadOnlySet<int> AllAddonIds { get; }

    /// <summary>
    /// Returns true if the combined (local + online) dependency graph contains a cycle.
    /// </summary>
    public bool HasCycle => _hasCycle;

    /// <summary>
    /// Computes the transitive closure of local dependencies for <paramref name="addonId"/>.
    /// Includes all recursive dependencies (dependencies of dependencies, etc.)
    /// but does NOT include <paramref name="addonId"/> itself.
    /// </summary>
    public IReadOnlySet<int> GetClosure(int addonId)
    {
        var visited = new HashSet<int>();
        var queue = new Queue<int>();

        if (_forwardLocal.TryGetValue(addonId, out var directDeps))
            foreach (var dep in directDeps)
                queue.Enqueue(dep);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
                continue;

            if (_forwardLocal.TryGetValue(current, out var transitiveDeps))
                foreach (var dep in transitiveDeps)
                    if (!visited.Contains(dep))
                        queue.Enqueue(dep);
        }

        return visited;
    }

    /// <summary>
    /// Computes the transitive closure using both local and online forward dependency maps.
    /// </summary>
    public IReadOnlySet<int> GetClosureCombined(int addonId)
    {
        var visited = new HashSet<int>();
        var queue = new Queue<int>();

        if (_forwardLocal.TryGetValue(addonId, out var directLocal))
            foreach (var dep in directLocal)
                queue.Enqueue(dep);

        if (_forwardOnline.TryGetValue(addonId, out var directOnline))
            foreach (var dep in directOnline)
                queue.Enqueue(dep);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
                continue;

            if (_forwardLocal.TryGetValue(current, out var transitiveLocal))
                foreach (var dep in transitiveLocal)
                    if (!visited.Contains(dep))
                        queue.Enqueue(dep);

            if (_forwardOnline.TryGetValue(current, out var transitiveOnline))
                foreach (var dep in transitiveOnline)
                    if (!visited.Contains(dep))
                        queue.Enqueue(dep);
        }

        return visited;
    }

    public static AddonGraphSnapshot Create(
        IReadOnlyDictionary<int, int[]> forwardLocalDependencies,
        IReadOnlyDictionary<int, int[]> forwardOnlineDependencies)
    {
        var allIds = new HashSet<int>();
        var fwdLocal = new Dictionary<int, IReadOnlySet<int>>();
        var fwdOnline = new Dictionary<int, IReadOnlySet<int>>();

        foreach (var (id, deps) in forwardLocalDependencies)
        {
            fwdLocal[id] = new HashSet<int>(deps);
            allIds.Add(id);
            foreach (var dep in deps)
                allIds.Add(dep);
        }

        foreach (var (id, deps) in forwardOnlineDependencies)
        {
            fwdOnline[id] = new HashSet<int>(deps);
            allIds.Add(id);
            foreach (var dep in deps)
                allIds.Add(dep);
        }

        var combined = CombineDependencyMaps(fwdLocal, fwdOnline);
        var hasCycle = DetectCycle(combined, allIds);

        return new AddonGraphSnapshot(
            ToReadOnly(fwdLocal),
            BuildReverseMap(fwdLocal),
            ToReadOnly(fwdOnline),
            BuildReverseMap(fwdOnline),
            allIds,
            hasCycle);
    }

    private static IReadOnlyDictionary<int, IReadOnlySet<int>> CombineDependencyMaps(
        Dictionary<int, IReadOnlySet<int>> local,
        Dictionary<int, IReadOnlySet<int>> online)
    {
        var combined = new Dictionary<int, HashSet<int>>();

        foreach (var (id, deps) in local) combined[id] = [.. deps];

        foreach (var (id, deps) in online)
            if (combined.TryGetValue(id, out var existing))
                foreach (var dep in deps)
                    existing.Add(dep);
            else
                combined[id] = [.. deps];

        return combined.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<int>)kv.Value);
    }

    /// <summary>
    /// Detects cycles in the dependency graph using DFS.
    /// </summary>
    private static bool DetectCycle(
        IReadOnlyDictionary<int, IReadOnlySet<int>> forward,
        HashSet<int> allIds)
    {
        var white = new HashSet<int>(allIds);
        var grey = new HashSet<int>();
        var black = new HashSet<int>();

        while (white.Count > 0)
        {
            var start = white.First();
            if (DfsHasCycle(start, forward, white, grey, black))
                return true;
        }

        return false;
    }

    private static bool DfsHasCycle(
        int node,
        IReadOnlyDictionary<int, IReadOnlySet<int>> forward,
        HashSet<int> white,
        HashSet<int> grey,
        HashSet<int> black)
    {
        if (black.Contains(node))
            return false;

        if (grey.Contains(node))
            return true;

        if (!white.Remove(node))
            return false;

        grey.Add(node);

        if (forward.TryGetValue(node, out var neighbors))
            foreach (var neighbor in neighbors)
                if (DfsHasCycle(neighbor, forward, white, grey, black))
                    return true;

        grey.Remove(node);
        black.Add(node);
        return false;
    }

    private static IReadOnlyDictionary<int, IReadOnlySet<int>> BuildReverseMap(
        Dictionary<int, IReadOnlySet<int>> forward)
    {
        var reverse = new Dictionary<int, HashSet<int>>();

        foreach (var (dependentId, dependencyIds) in forward)
        foreach (var depId in dependencyIds)
        {
            if (!reverse.TryGetValue(depId, out var set))
            {
                set = [];
                reverse[depId] = set;
            }

            set.Add(dependentId);
        }

        return reverse.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<int>)kv.Value);
    }

    private static IReadOnlyDictionary<int, IReadOnlySet<int>> ToReadOnly(
        Dictionary<int, IReadOnlySet<int>> source)
    {
        return source.ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}