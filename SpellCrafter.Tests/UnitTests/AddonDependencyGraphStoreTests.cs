using System.Linq;
using SpellCrafter.Data;
using SpellCrafter.Models;
using SpellCrafter.Services;
using SpellCrafter.Tests.TestInfrastructure;

namespace SpellCrafter.Tests.UnitTests;

public sealed class AddonDependencyGraphStoreTests : IDisposable
{
    private readonly FakeEsoDataConnectionFactory _dbFactory;

    public AddonDependencyGraphStoreTests()
    {
        _dbFactory = new FakeEsoDataConnectionFactory();
    }

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public void CreateSnapshot_EmptyDatabase_ReturnsEmptySnapshot()
    {
        var store = new AddonDependencyGraphStore(_dbFactory);
        var snapshot = store.CreateSnapshot();

        Assert.Empty(snapshot.ForwardLocalDependencies);
        Assert.Empty(snapshot.ForwardOnlineDependencies);
        Assert.Empty(snapshot.ReverseLocalDependencies);
        Assert.Empty(snapshot.ReverseOnlineDependencies);
    }

    [Fact]
    public void CreateSnapshot_WithLocalDependencies_BuildsCorrectMaps()
    {
        using var db = _dbFactory.CreateConnection();

        var common1 = new CommonAddon { Name = "AddonA", Title = "A" };
        var common2 = new CommonAddon { Name = "DepX", Title = "X" };
        var common3 = new CommonAddon { Name = "DepY", Title = "Y" };
        db.Insert(common1);
        db.Insert(common2);
        db.Insert(common3);

        var local1 = new LocalAddon { CommonAddonId = common1.Id, State = Enums.AddonState.LatestVersion };
        var local2 = new LocalAddon { CommonAddonId = common2.Id, State = Enums.AddonState.LatestVersion };
        var local3 = new LocalAddon { CommonAddonId = common3.Id, State = Enums.AddonState.LatestVersion };
        db.Insert(local1);
        db.Insert(local2);
        db.Insert(local3);

        // AddonA depends on DepX and DepY
        db.Insert(new LocalAddonDependency { LocalAddonId = local1.Id!.Value, DependentCommonAddonId = common2.Id });
        db.Insert(new LocalAddonDependency { LocalAddonId = local1.Id!.Value, DependentCommonAddonId = common3.Id });

        // DepX depends on DepY
        db.Insert(new LocalAddonDependency { LocalAddonId = local2.Id!.Value, DependentCommonAddonId = common3.Id });

        var store = new AddonDependencyGraphStore(_dbFactory);
        var snapshot = store.CreateSnapshot();

        // Forward: AddonA depends on DepX and DepY
        Assert.True(snapshot.ForwardLocalDependencies.ContainsKey(common1.Id));
        var depsOfA = snapshot.ForwardLocalDependencies[common1.Id].ToList();
        depsOfA.Sort();
        Assert.Equal([common2.Id, common3.Id], depsOfA);

        // Forward: DepX depends on DepY
        Assert.True(snapshot.ForwardLocalDependencies.ContainsKey(common2.Id));
        Assert.Equal([common3.Id], snapshot.ForwardLocalDependencies[common2.Id].ToList());

        // Forward: DepY has no dependencies
        Assert.False(snapshot.ForwardLocalDependencies.ContainsKey(common3.Id));

        // Reverse: DepX depended on by AddonA
        Assert.True(snapshot.ReverseLocalDependencies.ContainsKey(common2.Id));
        Assert.Contains(common1.Id, snapshot.ReverseLocalDependencies[common2.Id]);

        // Reverse: DepY depended on by AddonA and DepX
        Assert.True(snapshot.ReverseLocalDependencies.ContainsKey(common3.Id));
        Assert.Contains(common1.Id, snapshot.ReverseLocalDependencies[common3.Id]);
        Assert.Contains(common2.Id, snapshot.ReverseLocalDependencies[common3.Id]);
    }

    [Fact]
    public void CreateSnapshot_WithOnlineDependencies_BuildsCorrectMaps()
    {
        using var db = _dbFactory.CreateConnection();

        var common1 = new CommonAddon { Name = "OnlineA", Title = "A" };
        var common2 = new CommonAddon { Name = "OnlineDep", Title = "D" };
        db.Insert(common1);
        db.Insert(common2);

        var online1 = new OnlineAddon { CommonAddonId = common1.Id, LatestVersion = "1.0" };
        var online2 = new OnlineAddon { CommonAddonId = common2.Id, LatestVersion = "2.0" };
        db.Insert(online1);
        db.Insert(online2);

        db.Insert(new OnlineAddonDependency { OnlineAddonId = online1.Id, DependentCommonAddonId = common2.Id });

        var store = new AddonDependencyGraphStore(_dbFactory);
        var snapshot = store.CreateSnapshot();

        Assert.Contains(common1.Id, snapshot.ForwardOnlineDependencies.Keys);
        Assert.Equal([common2.Id], snapshot.ForwardOnlineDependencies[common1.Id].ToList());
    }

    [Fact]
    public void CreateSnapshot_WithNoDependencies_ReturnsEmptyMaps()
    {
        using var db = _dbFactory.CreateConnection();

        var common = new CommonAddon { Name = "Standalone", Title = "S" };
        db.Insert(common);

        var local = new LocalAddon { CommonAddonId = common.Id, State = Enums.AddonState.LatestVersion };
        db.Insert(local);

        var store = new AddonDependencyGraphStore(_dbFactory);
        var snapshot = store.CreateSnapshot();

        Assert.False(snapshot.ForwardLocalDependencies.ContainsKey(common.Id));
        Assert.Empty(snapshot.ReverseLocalDependencies);
    }
}