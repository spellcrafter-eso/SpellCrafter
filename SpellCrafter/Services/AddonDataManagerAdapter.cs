using System.Collections.Generic;
using SpellCrafter.Data;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

public sealed class AddonDataManagerAdapter : IAddonDataManager
{
    private readonly IEsoDataConnectionFactory _dbConnectionFactory;

    public AddonDataManagerAdapter()
        : this(new EsoDataConnectionFactory())
    {
    }

    public AddonDataManagerAdapter(IEsoDataConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory
                               ?? throw new System.ArgumentNullException(nameof(dbConnectionFactory));
    }

    public IReadOnlyList<Addon> OnlineAddons => AddonDataManager.OnlineAddons;

    public IReadOnlyList<Addon> InstalledAddons => AddonDataManager.InstalledAddons;

    public void InsertOrUpdateLocalAddon(Addon addon)
    {
        using var db = _dbConnectionFactory.CreateConnection();
        AddonDataManager.InsertOrUpdateLocalAddon(db, addon);
    }

    public void RemoveLocalAddon(Addon addon)
    {
        using var db = _dbConnectionFactory.CreateConnection();
        AddonDataManager.RemoveLocalAddon(db, addon);
    }
}