using SpellCrafter.Cli;
using SpellCrafter.Services;

namespace SpellCrafter.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="AddonResolver"/> methods.
///
/// These tests operate on the existing <see cref="AddonDataManager"/>
/// static collections. When run in parallel with other tests, the collections
/// may already contain data from the production DB (ESOAddons.db). To get
/// deterministic results, we:
///   1. Save and replace the static collections with controlled data.
///   2. Restore the originals in Dispose.
///   3. Use a custom collection definition to prevent parallel execution.
///
/// If the test runner detects "no tests available" or similar issues, run
/// these tests in isolation with:
///   dotnet test --filter "AddonResolver"
/// </summary>
[Collection("AddonResolverCollection")]
public sealed class AddonResolverTests : IDisposable
{
    private readonly RangedObservableCollection<Addon> _savedInstalled;
    private readonly RangedObservableCollection<Addon> _savedOnline;

    public AddonResolverTests()
    {
        // Accessing AddonDataManager triggers the static constructor.
        // Save the existing collections (may be from production DB or other tests).
        _savedInstalled = AddonDataManager.InstalledAddons;
        _savedOnline = AddonDataManager.OnlineAddons;

        // Replace with controlled test data
        AddonDataManager.InstalledAddons = new RangedObservableCollection<Addon>
        {
            new() { CommonAddonId = 1, Name = "InstalledAddon", UniqueId = 100 },
            new() { CommonAddonId = 2, Name = "SharedAddon", UniqueId = 200 }
        };

        AddonDataManager.OnlineAddons = new RangedObservableCollection<Addon>
        {
            new() { CommonAddonId = 3, Name = "OnlineOnly", UniqueId = 300 },
            new() { CommonAddonId = 2, Name = "SharedAddon", UniqueId = 200 }
        };
    }

    // --- ResolveInstalled ---

    [Fact]
    public void ResolveInstalled_ExactMatch_ReturnsAddon()
    {
        var result = AddonResolver.ResolveInstalled("InstalledAddon");
        Assert.NotNull(result);
        Assert.Equal(1, result!.CommonAddonId);
    }

    [Fact]
    public void ResolveInstalled_CaseInsensitive_ReturnsAddon()
    {
        var result = AddonResolver.ResolveInstalled("installedaddon");
        Assert.NotNull(result);
        Assert.Equal("InstalledAddon", result!.Name);
    }

    [Fact]
    public void ResolveInstalled_NotFound_ReturnsNull()
    {
        Assert.Null(AddonResolver.ResolveInstalled("NonExistent"));
    }

    // --- ResolveOnline ---

    [Fact]
    public void ResolveOnline_ExactMatch_ReturnsAddon()
    {
        var result = AddonResolver.ResolveOnline("OnlineOnly");
        Assert.NotNull(result);
        Assert.Equal(3, result!.CommonAddonId);
    }

    [Fact]
    public void ResolveOnline_CaseInsensitive_ReturnsAddon()
    {
        Assert.NotNull(AddonResolver.ResolveOnline("onlineonly"));
    }

    [Fact]
    public void ResolveOnline_NotFound_ReturnsNull()
    {
        Assert.Null(AddonResolver.ResolveOnline("NonExistent"));
    }

    // --- ResolveAny ---

    [Fact]
    public void ResolveAny_InstalledFirst()
    {
        var result = AddonResolver.ResolveAny("InstalledAddon");
        Assert.NotNull(result);
        Assert.Equal(1, result!.CommonAddonId);
    }

    [Fact]
    public void ResolveAny_OnlineOnly_ReturnsOnline()
    {
        var result = AddonResolver.ResolveAny("OnlineOnly");
        Assert.NotNull(result);
        Assert.Equal(3, result!.CommonAddonId);
    }

    [Fact]
    public void ResolveAny_NotFound_ReturnsNull()
    {
        Assert.Null(AddonResolver.ResolveAny("NonExistent"));
    }

    // --- ResolveById ---

    [Fact]
    public void ResolveById_MatchesInstalled()
    {
        var result = AddonResolver.ResolveById(100);
        Assert.NotNull(result);
        Assert.Equal("InstalledAddon", result!.Name);
    }

    [Fact]
    public void ResolveById_MatchesOnlineFirst()
    {
        var result = AddonResolver.ResolveById(300);
        Assert.NotNull(result);
        Assert.Equal("OnlineOnly", result!.Name);
    }

    [Fact]
    public void ResolveById_NotFound_ReturnsNull()
    {
        Assert.Null(AddonResolver.ResolveById(999));
    }

    // --- FindCandidates ---

    [Fact]
    public void FindCandidates_PartialName_ReturnsMatches()
    {
        var results = AddonResolver.FindCandidates("Installed");
        Assert.Contains(results, a => a.Name == "InstalledAddon");
    }

    [Fact]
    public void FindCandidates_NoMatch_ReturnsEmpty()
    {
        Assert.Empty(AddonResolver.FindCandidates("ZZZZ"));
    }

    [Fact]
    public void FindCandidates_SharedAddon_NotDuplicated()
    {
        var results = AddonResolver.FindCandidates("SharedAddon");
        Assert.Single(results);
    }

    public void Dispose()
    {
        AddonDataManager.InstalledAddons = _savedInstalled;
        AddonDataManager.OnlineAddons = _savedOnline;
    }
}

/// <summary>
/// Collection definition to serialize AddonResolver tests.
/// This prevents parallel execution with other tests that also write
/// to the static <see cref="AddonDataManager"/> collections.
/// </summary>
[CollectionDefinition("AddonResolverCollection", DisableParallelization = true)]
public sealed class AddonResolverCollectionDefinition;