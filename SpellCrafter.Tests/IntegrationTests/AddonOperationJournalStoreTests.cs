using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;
using SpellCrafter.Services;
using SpellCrafter.Tests.TestInfrastructure;

namespace SpellCrafter.Tests.IntegrationTests;

public sealed class AddonOperationJournalStoreTests : IDisposable
{
    private readonly FakeEsoDataConnectionFactory _dbFactory;
    private readonly AddonOperationJournalStoreAdapter _store;
    private readonly Addon _addon;
    private readonly Addon.AddonOperationPaths _paths;

    public AddonOperationJournalStoreTests()
    {
        _dbFactory = new FakeEsoDataConnectionFactory();
        _store = new AddonOperationJournalStoreAdapter(_dbFactory);

        _addon = new Addon
        {
            CommonAddonId = 42,
            UniqueId = 123,
            Name = "TestAddon",
            Version = "1.0",
            DisplayedVersion = "1.0.0",
            LatestVersion = "2.0",
            DisplayedLatestVersion = "2.0.0"
        };

        _paths = new Addon.AddonOperationPaths(
            Guid.NewGuid().ToString("N"),
            "/tmp/AddOns",
            "/tmp/Operations",
            "/tmp/Operations/op1",
            "/tmp/Operations/op1/download",
            "/tmp/Operations/op1/staging",
            "/tmp/Operations/op1/backup",
            "/tmp/AddOns/TestAddon",
            "/tmp/Operations/op1/staging/TestAddon",
            "/tmp/Operations/op1/backup/TestAddon");
    }

    [Fact]
    public void Begin_CreatesJournalWithCorrectFields()
    {
        var journal = _store.Begin(
            _addon, "Install", _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        Assert.NotNull(journal);
        Assert.Equal("TestAddon", journal.AddonName);
        Assert.Equal(42, journal.CommonAddonId);
        Assert.Equal(123, journal.UniqueId);
        Assert.Equal("Install", journal.OperationType);
        Assert.Equal(AddonOperationPhase.Created, journal.Phase);
        Assert.False(journal.IsComplete);
        Assert.False(journal.RequiresAttention);
        Assert.Equal(_paths.OperationId, journal.OperationId);
    }

    [Fact]
    public void SetPhase_UpdatesPhase()
    {
        var journal = _store.Begin(
            _addon, "Install", _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _store.SetPhase(journal, AddonOperationPhase.ExtractingToStaging);

        Assert.Equal(AddonOperationPhase.ExtractingToStaging, journal.Phase);

        // Verify persistence by reading back
        var incomplete = _store.GetIncompleteOperations();
        var reloaded = Assert.Single(incomplete);
        Assert.Equal(AddonOperationPhase.ExtractingToStaging, reloaded.Phase);
    }

    [Fact]
    public void SetPhaseWithError_SetsPhaseAndErrorMessage()
    {
        var journal = _store.Begin(
            _addon, "Install", _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _store.SetPhaseWithError(journal, AddonOperationPhase.ExtractingToStaging, "extraction failed");

        Assert.Equal(AddonOperationPhase.ExtractingToStaging, journal.Phase);
        Assert.Equal("extraction failed", journal.LastErrorMessage);
    }

    [Fact]
    public void Complete_SetsIsComplete()
    {
        var journal = _store.Begin(
            _addon, "Install", _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _store.Complete(journal);

        Assert.True(journal.IsComplete);
        Assert.Equal(AddonOperationPhase.Committed, journal.Phase);
        Assert.NotNull(journal.CompletedAtUtc);
    }

    [Fact]
    public void Complete_WithCustomPhase()
    {
        var journal = _store.Begin(
            _addon, "Delete", _paths,
            AddonState.LatestVersion, AddonInstallationMethod.SpellCrafter,
            "1.0", "1.0.0", true, AddonInstallationMethod.Other);

        _store.Complete(journal, AddonOperationPhase.RecoveryCompleted);

        Assert.True(journal.IsComplete);
        Assert.Equal(AddonOperationPhase.RecoveryCompleted, journal.Phase);
    }

    [Fact]
    public void GetIncompleteOperations_ReturnsOnlyIncomplete()
    {
        var j1 = _store.Begin(
            _addon, "Install", _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        var addon2 = new Addon { CommonAddonId = 43, Name = "Addon2" };
        var paths2 = _paths with { OperationId = Guid.NewGuid().ToString("N") };
        var j2 = _store.Begin(
            addon2, "Install", paths2,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _store.Complete(j1);

        var incomplete = _store.GetIncompleteOperations();
        Assert.Single(incomplete);
        Assert.Equal("Addon2", incomplete[0].AddonName);
    }

    [Fact]
    public void CancelRolledBack_MarksCompleteAndNotAttention()
    {
        var journal = _store.Begin(
            _addon, "Install", _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _store.MarkRequiresAttention(journal, "needs attention");
        _store.CancelRolledBack(journal, "rolled back by user");

        Assert.True(journal.IsComplete);
        Assert.False(journal.RequiresAttention);
        Assert.Equal(AddonOperationPhase.CanceledRolledBack, journal.Phase);
    }

    [Fact]
    public void FailRolledBack_MarksCompleteWithError()
    {
        var journal = _store.Begin(
            _addon, "Install", _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _store.FailRolledBack(journal, "disk full");

        Assert.True(journal.IsComplete);
        Assert.Equal(AddonOperationPhase.FailedRolledBack, journal.Phase);
        Assert.Equal("disk full", journal.LastErrorMessage);
    }

    [Fact]
    public void MarkRequiresAttention_DoesNotMarkComplete()
    {
        var journal = _store.Begin(
            _addon, "Install", _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _store.MarkRequiresAttention(journal, "needs manual review");

        Assert.True(journal.RequiresAttention);
        Assert.False(journal.IsComplete);
    }

    [Fact]
    public void GetAttentionOperations_ReturnsOnlyAttention()
    {
        var j1 = _store.Begin(
            _addon, "Install", _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _store.MarkRequiresAttention(j1, "needs review");

        var addon2 = new Addon { CommonAddonId = 44, Name = "GoodAddon" };
        var paths2 = _paths with { OperationId = Guid.NewGuid().ToString("N") };
        var j2 = _store.Begin(
            addon2, "Install", paths2,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _store.Complete(j2);

        var attention = _store.GetAttentionOperations();
        var j = Assert.Single(attention);
        Assert.Equal("TestAddon", j.AddonName);
    }

    [Fact]
    public void MarkRecoveryFailed_SetsCorrectState()
    {
        var journal = _store.Begin(
            _addon, "Install", _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _store.MarkRecoveryFailed(journal, "validation failed", "could not determine path");

        Assert.True(journal.IsComplete);
        Assert.True(journal.RequiresAttention);
        Assert.Equal(AddonOperationPhase.RecoveryFailed, journal.Phase);
        Assert.Equal("validation failed", journal.LastErrorMessage);
        Assert.Equal("could not determine path", journal.RecoveryMessage);
    }

    [Fact]
    public void CompleteWithAttention_SetsRequiresAttention()
    {
        var journal = _store.Begin(
            _addon, "Install", _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _store.CompleteWithAttention(journal, AddonOperationPhase.RecoveryCompleted, "repaired with warnings");

        Assert.True(journal.IsComplete);
        Assert.True(journal.RequiresAttention);
        Assert.Equal(AddonOperationPhase.RecoveryCompleted, journal.Phase);
        Assert.Equal("repaired with warnings", journal.RecoveryMessage);
    }

    public void Dispose()
    {
        _dbFactory.Dispose();
    }
}