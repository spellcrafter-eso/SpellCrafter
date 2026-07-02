using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Tests.UnitTests;

public sealed class AddonOperationPlannerTests
{
    private static AddonGraphSnapshot MakeSnapshotEmpty()
    {
        return AddonGraphSnapshot.Create(
            new Dictionary<int, int[]>(),
            new Dictionary<int, int[]>());
    }

    /// <summary>
    /// 1 ← A
    /// 2 ← A
    /// 3 ← B
    /// where:
    ///   A (id=10) depends on 1
    ///   B (id=20) depends on 3
    ///   C (id=30) has no deps
    /// </summary>
    private static AddonGraphSnapshot MakeSnapshotInstallScenario()
    {
        var local = new Dictionary<int, int[]>
        {
            { 10, [1] }, // A depends on X
            { 20, [3] }, // B depends on Z
            { 30, [] } // C has no deps
        };
        return AddonGraphSnapshot.Create(local, new Dictionary<int, int[]>());
    }

    [Fact]
    public void BuildPlan_InstallNonRecursive_CreatesSingleAction()
    {
        var snapshot = MakeSnapshotEmpty();
        var request = new AddonOperationRequest(
            AddonOperationType.Install, 30, "C", false,
            AddonInstallationMethod.SpellCrafter, null);

        var plan = AddonOperationPlanner.BuildPlan(request, snapshot);

        Assert.Single(plan.Actions);
        Assert.Equal(30, plan.Actions[0].CommonAddonId);
        Assert.Equal(AddonOperationType.Install, plan.Actions[0].OperationType);
        Assert.True(plan.Actions[0].IsRoot);
        Assert.False(plan.HasCycle);
    }

    [Fact]
    public void BuildPlan_InstallRecursiveWithDeps_IncludesDependencies()
    {
        var snapshot = MakeSnapshotInstallScenario();
        var request = new AddonOperationRequest(
            AddonOperationType.Install, 10, "A", true,
            AddonInstallationMethod.SpellCrafter, null);

        var plan = AddonOperationPlanner.BuildPlan(request, snapshot);

        Assert.Equal(2, plan.Actions.Count);
        Assert.Contains(plan.Actions, a => a.IsRoot && a.CommonAddonId == 10);
        Assert.Contains(plan.Actions, a => !a.IsRoot && a.CommonAddonId == 1);
    }

    [Fact]
    public void BuildPlan_InstallRecursiveWithNoDeps_SingleAction()
    {
        var snapshot = MakeSnapshotInstallScenario();
        var request = new AddonOperationRequest(
            AddonOperationType.Install, 30, "C", true,
            AddonInstallationMethod.SpellCrafter, null);

        var plan = AddonOperationPlanner.BuildPlan(request, snapshot);

        Assert.Single(plan.Actions);
        Assert.Equal(30, plan.Actions[0].CommonAddonId);
    }

    [Fact]
    public void BuildPlan_ResourcesIncludeRootAndDependencies()
    {
        var snapshot = MakeSnapshotInstallScenario();
        var request = new AddonOperationRequest(
            AddonOperationType.Install, 10, "A", true,
            AddonInstallationMethod.SpellCrafter, null);

        var plan = AddonOperationPlanner.BuildPlan(request, snapshot);

        var addonKeys = new List<int>();
        foreach (var r in plan.Resources)
            if (r.Kind == "addon")
                addonKeys.Add(int.Parse(r.Key));

        Assert.Contains(10, addonKeys);
        Assert.Contains(1, addonKeys);
    }

    [Fact]
    public void BuildPlan_DeleteRootOnly_DoesNotCheckDependents()
    {
        var snapshot = MakeSnapshotEmpty();
        var request = new AddonOperationRequest(
            AddonOperationType.Delete, 30, "C", false,
            AddonInstallationMethod.SpellCrafter, null);

        var plan = AddonOperationPlanner.BuildPlan(request, snapshot);

        Assert.Single(plan.Actions);
        Assert.Equal(AddonOperationType.Delete, plan.Actions[0].OperationType);
    }

    [Fact]
    public void BuildPlan_DeleteWithReverseDependents_Rejects()
    {
        var local = new Dictionary<int, int[]>
        {
            { 20, [3] },
            { 3, [] }
        };
        var snapshot = AddonGraphSnapshot.Create(local, new Dictionary<int, int[]>());
        var request = new AddonOperationRequest(
            AddonOperationType.Delete, 3, "X", false,
            AddonInstallationMethod.SpellCrafter, null);

        var plan = AddonOperationPlanner.BuildPlan(request, snapshot);

        Assert.True(plan.IsRejected);
        Assert.NotNull(plan.RejectionReason);
    }

    [Fact]
    public void BuildPlan_DeleteWithCascade_IncludesOrphanedDeps()
    {
        var local = new Dictionary<int, int[]>
        {
            { 10, [1] },
            { 1, [] }
        };
        var snapshot = AddonGraphSnapshot.Create(local, new Dictionary<int, int[]>());
        var request = new AddonOperationRequest(
            AddonOperationType.Delete, 10, "A", false,
            AddonInstallationMethod.SpellCrafter,
            new DeleteOperationOptions(true));

        var plan = AddonOperationPlanner.BuildPlan(request, snapshot);

        Assert.Equal(2, plan.Actions.Count);
        Assert.Contains(plan.Actions, a => a.CommonAddonId == 10);
        Assert.Contains(plan.Actions, a => a.CommonAddonId == 1);
    }

    [Fact]
    public void BuildPlan_DeleteWithCascade_PreservesSharedDependency()
    {
        var local = new Dictionary<int, int[]>
        {
            { 10, [1] },
            { 20, [1] },
            { 1, [] }
        };
        var snapshot = AddonGraphSnapshot.Create(local, new Dictionary<int, int[]>());
        var request = new AddonOperationRequest(
            AddonOperationType.Delete, 10, "A", false,
            AddonInstallationMethod.SpellCrafter,
            new DeleteOperationOptions(true));

        var plan = AddonOperationPlanner.BuildPlan(request, snapshot);

        Assert.Single(plan.Actions);
        Assert.Equal(10, plan.Actions[0].CommonAddonId);
    }

    [Fact]
    public void BuildPlan_WithCyclicDependency_DetectsCycle()
    {
        var local = new Dictionary<int, int[]>
        {
            { 1, [2] },
            { 2, [1] }
        };
        var snapshot = AddonGraphSnapshot.Create(local, new Dictionary<int, int[]>());
        var request = new AddonOperationRequest(
            AddonOperationType.Install, 1, "A", true,
            AddonInstallationMethod.SpellCrafter, null);

        var plan = AddonOperationPlanner.BuildPlan(request, snapshot);

        Assert.True(plan.HasCycle);
    }
}