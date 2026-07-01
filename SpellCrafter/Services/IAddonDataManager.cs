using System.Collections.Generic;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

public interface IAddonDataManager
{
    IReadOnlyList<Addon> OnlineAddons { get; }

    IReadOnlyList<Addon> InstalledAddons { get; }

    void InsertOrUpdateLocalAddon(Addon addon);

    void RemoveLocalAddon(Addon addon);
}
