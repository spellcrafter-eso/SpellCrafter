using System.Collections.Generic;
using System.Linq;
using SpellCrafter.Data;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

/// <summary>
/// Builds immutable snapshots of the addon dependency graph from the SQLite database.
/// </summary>
public sealed class AddonDependencyGraphStore : IAddonDependencyGraphStore
{
    private readonly IEsoDataConnectionFactory _dbConnectionFactory;

    public AddonDependencyGraphStore(IEsoDataConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory
                               ?? throw new System.ArgumentNullException(nameof(dbConnectionFactory));
    }

    public AddonGraphSnapshot CreateSnapshot()
    {
        using var db = _dbConnectionFactory.CreateConnection();

        var forwardLocal = BuildForwardLocalDependencies(db);
        var forwardOnline = BuildForwardOnlineDependencies(db);

        return AddonGraphSnapshot.Create(forwardLocal, forwardOnline);
    }

    private static Dictionary<int, int[]> BuildForwardLocalDependencies(EsoDataConnection db)
    {
        var localAddons = db.Table<LocalAddon>().ToList();
        var allDeps = db.Table<LocalAddonDependency>().ToList();

        // Build lookup: LocalAddonId -> list of DependentCommonAddonId
        var depsByLocalAddonId = allDeps
            .GroupBy(d => d.LocalAddonId)
            .ToDictionary(g => g.Key, g => g.Select(d => d.DependentCommonAddonId).ToList());

        var forward = new Dictionary<int, int[]>();

        foreach (var local in localAddons)
            if (local.Id != null &&
                depsByLocalAddonId.TryGetValue(local.Id.Value, out var depIds) &&
                depIds.Count > 0)
                forward[local.CommonAddonId] = depIds.ToArray();

        return forward;
    }

    private static Dictionary<int, int[]> BuildForwardOnlineDependencies(EsoDataConnection db)
    {
        var onlineAddons = db.Table<OnlineAddon>().ToList();
        var allDeps = db.Table<OnlineAddonDependency>().ToList();

        var depsByOnlineAddonId = allDeps
            .GroupBy(d => d.OnlineAddonId)
            .ToDictionary(g => g.Key, g => g.Select(d => d.DependentCommonAddonId).ToList());

        var forward = new Dictionary<int, int[]>();

        foreach (var online in onlineAddons)
            if (depsByOnlineAddonId.TryGetValue(online.Id, out var depIds) &&
                depIds.Count > 0)
                forward[online.CommonAddonId] = depIds.ToArray();

        return forward;
    }
}