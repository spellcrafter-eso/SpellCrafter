using System.Linq;
using SpellCrafter.Models;

namespace SpellCrafter.Tests.UnitTests;

public sealed class AddonGraphSnapshotTests
{
    [Fact]
    public void ForwardLocalDependencies_ReturnsProvidedMap()
    {
        var forward = new Dictionary<int, int[]>
        {
            { 1, [2, 3] },
            { 2, [4] },
            { 3, [] },
            { 4, [] }
        };

        var snapshot = AddonGraphSnapshot.Create(forward, new Dictionary<int, int[]>());

        Assert.Equal(forward[1], snapshot.ForwardLocalDependencies[1]);
        Assert.Equal(forward[2], snapshot.ForwardLocalDependencies[2]);
        Assert.Equal(forward[3], snapshot.ForwardLocalDependencies[3]);
    }

    [Fact]
    public void ForwardOnlineDependencies_ReturnsProvidedMap()
    {
        var forward = new Dictionary<int, int[]>
        {
            { 1, [2] }
        };

        var snapshot = AddonGraphSnapshot.Create(new Dictionary<int, int[]>(), forward);

        Assert.Equal(forward[1], snapshot.ForwardOnlineDependencies[1]);
    }

    [Fact]
    public void ReverseLocalDependencies_DerivesCorrectMap()
    {
        var forward = new Dictionary<int, int[]>
        {
            { 1, [2, 3] },
            { 2, [3] },
            { 3, [] }
        };

        var snapshot = AddonGraphSnapshot.Create(forward, new Dictionary<int, int[]>());

        Assert.Contains(1, snapshot.ReverseLocalDependencies[2]);
        Assert.Contains(1, snapshot.ReverseLocalDependencies[3]);
        Assert.Contains(2, snapshot.ReverseLocalDependencies[3]);
        // Addon 1 (depended on by no one) should not be in the reverse map
        Assert.False(snapshot.ReverseLocalDependencies.ContainsKey(1));
    }

    [Fact]
    public void ReverseLocalDependencies_EmptyWhenNoDependents()
    {
        var forward = new Dictionary<int, int[]>
        {
            { 1, [] }
        };

        var snapshot = AddonGraphSnapshot.Create(forward, new Dictionary<int, int[]>());

        Assert.Empty(snapshot.ReverseLocalDependencies);
    }

    [Fact]
    public void ReverseLocalDependencies_MultipleAddonsDependOnSame()
    {
        var forward = new Dictionary<int, int[]>
        {
            { 1, [3] },
            { 2, [3] },
            { 3, [] }
        };

        var snapshot = AddonGraphSnapshot.Create(forward, new Dictionary<int, int[]>());

        var dependents = snapshot.ReverseLocalDependencies[3].ToList();
        dependents.Sort();
        Assert.Equal([1, 2], dependents);
    }

    [Fact]
    public void GetClosure_EmptyForAddonWithNoDependencies()
    {
        var forward = new Dictionary<int, int[]>
        {
            { 1, [] }
        };

        var snapshot = AddonGraphSnapshot.Create(forward, new Dictionary<int, int[]>());
        var closure = snapshot.GetClosure(1);

        Assert.Empty(closure);
    }

    [Fact]
    public void GetClosure_DirectDependency()
    {
        var forward = new Dictionary<int, int[]>
        {
            { 1, [2] },
            { 2, [] }
        };

        var snapshot = AddonGraphSnapshot.Create(forward, new Dictionary<int, int[]>());
        var closure = snapshot.GetClosure(1);

        Assert.Equal([2], closure.OrderBy(x => x).ToList());
    }

    [Fact]
    public void GetClosure_TransitiveDependencies()
    {
        var forward = new Dictionary<int, int[]>
        {
            { 1, [2] },
            { 2, [3] },
            { 3, [] }
        };

        var snapshot = AddonGraphSnapshot.Create(forward, new Dictionary<int, int[]>());
        var closure = snapshot.GetClosure(1);

        Assert.Equal([2, 3], closure.OrderBy(x => x).ToList());
    }

    [Fact]
    public void GetClosure_DiamondDependency()
    {
        var forward = new Dictionary<int, int[]>
        {
            { 1, [2, 3] },
            { 2, [4] },
            { 3, [4] },
            { 4, [] }
        };

        var snapshot = AddonGraphSnapshot.Create(forward, new Dictionary<int, int[]>());
        var closure = snapshot.GetClosure(1);

        Assert.Equal([2, 3, 4], closure.OrderBy(x => x).ToList());
    }

    [Fact]
    public void HasCycle_ReturnsFalseForAcyclicGraph()
    {
        var forward = new Dictionary<int, int[]>
        {
            { 1, [2] },
            { 2, [3] },
            { 3, [] }
        };

        var snapshot = AddonGraphSnapshot.Create(forward, new Dictionary<int, int[]>());

        Assert.False(snapshot.HasCycle);
    }

    [Fact]
    public void HasCycle_ReturnsTrueForSelfLoop()
    {
        var forward = new Dictionary<int, int[]>
        {
            { 1, [1] }
        };

        var snapshot = AddonGraphSnapshot.Create(forward, new Dictionary<int, int[]>());

        Assert.True(snapshot.HasCycle);
    }

    [Fact]
    public void HasCycle_ReturnsTrueForSimpleCycle()
    {
        var forward = new Dictionary<int, int[]>
        {
            { 1, [2] },
            { 2, [1] }
        };

        var snapshot = AddonGraphSnapshot.Create(forward, new Dictionary<int, int[]>());

        Assert.True(snapshot.HasCycle);
    }

    [Fact]
    public void HasCycle_ReturnsTrueForTransitiveCycle()
    {
        var forward = new Dictionary<int, int[]>
        {
            { 1, [2] },
            { 2, [3] },
            { 3, [1] }
        };

        var snapshot = AddonGraphSnapshot.Create(forward, new Dictionary<int, int[]>());

        Assert.True(snapshot.HasCycle);
    }

    [Fact]
    public void AllAddonIds_ReturnsUnionOfKeys()
    {
        var local = new Dictionary<int, int[]>
        {
            { 1, [2] },
            { 2, [] }
        };

        var online = new Dictionary<int, int[]>
        {
            { 3, [1] },
            { 4, [] }
        };

        var snapshot = AddonGraphSnapshot.Create(local, online);

        var allIds = snapshot.AllAddonIds.ToList();
        allIds.Sort();
        Assert.Equal([1, 2, 3, 4], allIds);
    }
}