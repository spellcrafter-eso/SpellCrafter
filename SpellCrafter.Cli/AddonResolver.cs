using System;
using System.Collections.Generic;
using System.Linq;
using SpellCrafter.Models;
using SpellCrafter.Services;

namespace SpellCrafter.Cli;

internal static class AddonResolver
{
    public static Addon? ResolveInstalled(string name)
    {
        return AddonDataManager.InstalledAddons.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static Addon? ResolveOnline(string name)
    {
        return AddonDataManager.OnlineAddons.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static Addon? ResolveAny(string name)
    {
        return ResolveInstalled(name) ?? ResolveOnline(name);
    }

    public static Addon? ResolveById(int uniqueId)
    {
        return AddonDataManager.OnlineAddons.FirstOrDefault(a => a.UniqueId == uniqueId)
               ?? AddonDataManager.InstalledAddons.FirstOrDefault(a => a.UniqueId == uniqueId);
    }

    public static List<Addon> FindCandidates(string name)
    {
        var results = new List<Addon>();

        results.AddRange(AddonDataManager.InstalledAddons
            .Where(a => a.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));

        results.AddRange(AddonDataManager.OnlineAddons
            .Where(a => a.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                        && results.All(r => r.CommonAddonId != a.CommonAddonId)));

        return results;
    }
}