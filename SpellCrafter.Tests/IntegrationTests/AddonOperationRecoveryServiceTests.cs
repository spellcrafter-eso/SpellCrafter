using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;
using SpellCrafter.Services;

namespace SpellCrafter.Tests.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="AddonOperationRecoveryService"/>.
/// Each test creates a temp directory and DB, writes journal entries directly,
/// and verifies recovery behavior.
/// </summary>
[Collection("AddonOperationRecoveryService")] // Serialize to avoid static state conflicts
public sealed class AddonOperationRecoveryServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _tmp;
    private readonly FakeEsoDataConnectionFactory _dbFactory;
    private readonly AddonOperationJournalStoreAdapter _journalStore;
    private readonly AddonOperationRecoveryService _recovery;
    private readonly EsoDataConnection _db;
    private readonly Addon _addon;
    private readonly Addon.AddonOperationPaths _paths;

    public AddonOperationRecoveryServiceTests()
    {
        _tmp = new TempDirectoryFixture();
        _dbFactory = new FakeEsoDataConnectionFactory();
        _journalStore = new AddonOperationJournalStoreAdapter(_dbFactory);
        _recovery = new AddonOperationRecoveryService(_journalStore);
        _db = _dbFactory.CreateConnection();

        _addon = new Addon
        {
            CommonAddonId = 50,
            UniqueId = 500,
            Name = "RecoveryAddon",
            Version = "1.0",
            DisplayedVersion = "1.0.0",
            LatestVersion = "1.1",
            DisplayedLatestVersion = "1.1.0"
        };

        var opId = Guid.NewGuid().ToString("N");
        _paths = new Addon.AddonOperationPaths(
            opId,
            _tmp.AddonsPath,
            Path.Combine(_tmp.RootPath, ".SpellCrafterOperations"),
            Path.Combine(_tmp.RootPath, ".SpellCrafterOperations", $"Install-RecoveryAddon-{opId}"),
            Path.Combine(_tmp.RootPath, ".SpellCrafterOperations", $"Install-RecoveryAddon-{opId}", "download"),
            Path.Combine(_tmp.RootPath, ".SpellCrafterOperations", $"Install-RecoveryAddon-{opId}", "staging"),
            Path.Combine(_tmp.RootPath, ".SpellCrafterOperations", $"Install-RecoveryAddon-{opId}", "backup"),
            Path.Combine(_tmp.AddonsPath, "RecoveryAddon"),
            Path.Combine(_tmp.AddonsPath, "RecoveryAddon"), // updated below
            Path.Combine(_tmp.RootPath, ".SpellCrafterOperations", $"Install-RecoveryAddon-{opId}", "backup", "RecoveryAddon"));
    }

    [Fact]
    public void NoIncompleteOperations_NoNotices()
    {
        // Clear any notices from previous tests
        AddonOperationRecoveryService.Notices.Clear();

        _recovery.RecoverIncompleteOperations(_db);

        Assert.Empty(AddonOperationRecoveryService.Notices);
    }

    [Fact]
    public void IncompleteInstall_BeforeStagedTargetMove_RollsBack()
    {
        // Create a journal entry in the Created phase
        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Install, _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        // Create the staging directory to simulate partial extraction
        Directory.CreateDirectory(_paths.StagingDirectory);

        _recovery.RecoverIncompleteOperations(_db);

        // Should have a notice
        Assert.NotEmpty(AddonOperationRecoveryService.Notices);

        // Journal should be completed (rolled back)
        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.DoesNotContain(incomplete, j => j.OperationId == _paths.OperationId);

        // Staging directory should be cleaned up
        Assert.False(Directory.Exists(_paths.RootDirectory));
    }

    [Fact]
    public void IncompleteInstall_AfterTargetMove_Completes()
    {
        // Create a journal entry and simulate that the staged move was done
        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Install, _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        // Set phase to AfterStagedTargetMove
        _journalStore.SetPhase(journal, AddonOperationPhase.AfterStagedTargetMove);

        // Create the target directory (as if Directory.Move succeeded)
        Directory.CreateDirectory(_paths.TargetAddonDirectory);
        File.WriteAllText(Path.Combine(_paths.TargetAddonDirectory, "test.txt"), "content");

        _recovery.RecoverIncompleteOperations(_db);

        // Journal should be completed
        Assert.NotEmpty(AddonOperationRecoveryService.Notices);
        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.DoesNotContain(incomplete, j => j.OperationId == _paths.OperationId);
    }

    [Fact]
    public void IncompleteDelete_AfterBackup_RestoresTarget()
    {
        // Create a delete journal entry at AfterTargetBackupForDelete phase
        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Delete, _paths,
            AddonState.LatestVersion, AddonInstallationMethod.SpellCrafter,
            "1.0", "1.0.0", true, AddonInstallationMethod.Other);

        _journalStore.SetPhase(journal, AddonOperationPhase.AfterTargetBackupForDelete);

        // Create backup directory (target was moved to backup)
        Directory.CreateDirectory(_paths.BackupAddonDirectory);
        File.WriteAllText(Path.Combine(_paths.BackupAddonDirectory, "saved.txt"), "data");

        _recovery.RecoverIncompleteOperations(_db);

        // Target should be restored from backup
        Assert.True(Directory.Exists(_paths.TargetAddonDirectory));
        Assert.True(File.Exists(Path.Combine(_paths.TargetAddonDirectory, "saved.txt")));

        // Journal completed
        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.DoesNotContain(incomplete, j => j.OperationId == _paths.OperationId);
    }

    [Fact]
    public void AttentionOperation_CreatesWarningNotice()
    {
        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Install, _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _journalStore.MarkRequiresAttention(journal, "manual review required");

        // Clear any prior notices
        AddonOperationRecoveryService.Notices.Clear();

        _recovery.RecoverIncompleteOperations(_db);

        // Should have a warning notice
        Assert.Contains(AddonOperationRecoveryService.Notices,
            n => n.Type == MessageDialogType.Warning &&
                 n.Message.Contains("RecoveryAddon") &&
                 n.Message.Contains("manual review"));
    }

    [Fact]
    public void InvalidJournalPaths_MarksRecoveryFailed()
    {
        // Create journal with unsafe paths
        var badPaths = _paths with { AddonsRootDirectory = "" };
        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Install, badPaths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _recovery.RecoverIncompleteOperations(_db);

        // Journal should be marked as recovery failed
        var attention = _journalStore.GetAttentionOperations();
        Assert.Contains(attention, j => j.OperationId == _paths.OperationId &&
                                        j.Phase == AddonOperationPhase.RecoveryFailed);
    }

    [Fact]
    public void UnknownOperationType_MarksRecoveryFailed()
    {
        var journal = _journalStore.Begin(
            _addon, "unknown_op", _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _recovery.RecoverIncompleteOperations(_db);

        var attention = _journalStore.GetAttentionOperations();
        Assert.Contains(attention, j => j.OperationId == _paths.OperationId &&
                                        j.Phase == AddonOperationPhase.RecoveryFailed);
    }

    [Fact]
    public void Recovery_Install_OnCreated_RollsBackStaging()
    {
        AddonOperationRecoveryService.Notices.Clear();

        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Install, _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        // Simulate partial extraction
        Directory.CreateDirectory(_paths.StagingDirectory);

        _recovery.RecoverIncompleteOperations(_db);

        Assert.NotEmpty(AddonOperationRecoveryService.Notices);
        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.DoesNotContain(incomplete, j => j.OperationId == _paths.OperationId);
        Assert.False(Directory.Exists(_paths.RootDirectory));
    }

    [Fact]
    public void Recovery_Install_OnBeforeStagedTargetMove_RollsBack()
    {
        AddonOperationRecoveryService.Notices.Clear();

        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Install, _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _journalStore.SetPhase(journal, AddonOperationPhase.BeforeStagedTargetMove);
        Directory.CreateDirectory(_paths.StagingDirectory);

        _recovery.RecoverIncompleteOperations(_db);

        Assert.NotEmpty(AddonOperationRecoveryService.Notices);
        Assert.Contains(AddonOperationRecoveryService.Notices,
            n => n.Message.Contains("rolled back"));
        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.DoesNotContain(incomplete, j => j.OperationId == _paths.OperationId);
        Assert.False(Directory.Exists(_paths.RootDirectory));
    }

    [Fact]
    public void Recovery_Install_OnAfterStagedTargetMove_Completes()
    {
        AddonOperationRecoveryService.Notices.Clear();

        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Install, _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _journalStore.SetPhase(journal, AddonOperationPhase.AfterStagedTargetMove);
        Directory.CreateDirectory(_paths.TargetAddonDirectory);
        File.WriteAllText(Path.Combine(_paths.TargetAddonDirectory, "test.txt"), "content");

        _recovery.RecoverIncompleteOperations(_db);

        Assert.True(Directory.Exists(_paths.TargetAddonDirectory));
        Assert.True(File.Exists(Path.Combine(_paths.TargetAddonDirectory, "test.txt")));
        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.DoesNotContain(incomplete, j => j.OperationId == _paths.OperationId);
        Assert.Contains(AddonOperationRecoveryService.Notices,
            n => n.Message.Contains("completed"));
    }

    [Fact]
    public void Recovery_Install_OnBeforeDatabaseCommit_Completes()
    {
        AddonOperationRecoveryService.Notices.Clear();

        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Install, _paths,
            AddonState.NotInstalled, AddonInstallationMethod.Other,
            "", "", false, AddonInstallationMethod.SpellCrafter);

        _journalStore.SetPhase(journal, AddonOperationPhase.BeforeDatabaseCommit);
        Directory.CreateDirectory(_paths.TargetAddonDirectory);

        _recovery.RecoverIncompleteOperations(_db);

        Assert.True(Directory.Exists(_paths.TargetAddonDirectory));
        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.DoesNotContain(incomplete, j => j.OperationId == _paths.OperationId);
    }

    [Fact]
    public void Recovery_Update_OnCreated_RollsBack()
    {
        AddonOperationRecoveryService.Notices.Clear();

        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Update, _paths,
            AddonState.LatestVersion, AddonInstallationMethod.SpellCrafter,
            "1.0", "1.0.0", true, AddonInstallationMethod.Other);

        _recovery.RecoverIncompleteOperations(_db);

        Assert.NotEmpty(AddonOperationRecoveryService.Notices);
        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.DoesNotContain(incomplete, j => j.OperationId == _paths.OperationId);
    }

    [Fact]
    public void Recovery_Update_OnAfterExistingTargetBackup_RestoresBackup()
    {
        AddonOperationRecoveryService.Notices.Clear();

        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Update, _paths,
            AddonState.LatestVersion, AddonInstallationMethod.SpellCrafter,
            "1.0", "1.0.0", true, AddonInstallationMethod.Other);

        _journalStore.SetPhase(journal, AddonOperationPhase.AfterExistingTargetBackup);
        Directory.CreateDirectory(_paths.BackupAddonDirectory);
        File.WriteAllText(Path.Combine(_paths.BackupAddonDirectory, "saved.txt"), "data");

        _recovery.RecoverIncompleteOperations(_db);

        Assert.True(Directory.Exists(_paths.TargetAddonDirectory));
        Assert.True(File.Exists(Path.Combine(_paths.TargetAddonDirectory, "saved.txt")));
        Assert.False(Directory.Exists(_paths.BackupAddonDirectory));
        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.DoesNotContain(incomplete, j => j.OperationId == _paths.OperationId);
        Assert.Contains(AddonOperationRecoveryService.Notices,
            n => n.Message.Contains("restored"));
    }

    [Fact]
    public void Recovery_Delete_OnBeforeTargetBackupForDelete_JustCompletes()
    {
        AddonOperationRecoveryService.Notices.Clear();

        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Delete, _paths,
            AddonState.LatestVersion, AddonInstallationMethod.SpellCrafter,
            "1.0", "1.0.0", true, AddonInstallationMethod.Other);

        _journalStore.SetPhase(journal, AddonOperationPhase.BeforeTargetBackupForDelete);
        Directory.CreateDirectory(_paths.TargetAddonDirectory);

        _recovery.RecoverIncompleteOperations(_db);

        Assert.True(Directory.Exists(_paths.TargetAddonDirectory));
        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.DoesNotContain(incomplete, j => j.OperationId == _paths.OperationId);
        Assert.Contains(AddonOperationRecoveryService.Notices,
            n => n.Message.Contains("rolled back") || n.Message.Contains("canceled"));
    }

    [Fact]
    public void Recovery_Delete_OnAfterTargetBackupForDelete_MissingBoth_Completes()
    {
        AddonOperationRecoveryService.Notices.Clear();

        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Delete, _paths,
            AddonState.LatestVersion, AddonInstallationMethod.SpellCrafter,
            "1.0", "1.0.0", true, AddonInstallationMethod.Other);

        _journalStore.SetPhase(journal, AddonOperationPhase.AfterTargetBackupForDelete);

        // Neither target nor backup exist
        _recovery.RecoverIncompleteOperations(_db);

        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.DoesNotContain(incomplete, j => j.OperationId == _paths.OperationId);
        var attention = _journalStore.GetAttentionOperations();
        Assert.DoesNotContain(attention, j => j.OperationId == _paths.OperationId);
    }

    [Fact]
    public void Recovery_Update_OnUnrecognizedPhase_MarksRecoveryFailed()
    {
        AddonOperationRecoveryService.Notices.Clear();

        var journal = _journalStore.Begin(
            _addon, AddonOperationType.Update, _paths,
            AddonState.LatestVersion, AddonInstallationMethod.SpellCrafter,
            "1.0", "1.0.0", true, AddonInstallationMethod.Other);

        _journalStore.SetPhase(journal, "Committed");

        _recovery.RecoverIncompleteOperations(_db);

        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.DoesNotContain(incomplete, j => j.OperationId == _paths.OperationId);
        var attention = _journalStore.GetAttentionOperations();
        Assert.Contains(attention, j => j.OperationId == _paths.OperationId &&
                                        j.Phase == AddonOperationPhase.RecoveryFailed);
    }

    public void Dispose()
    {
        _db.Dispose();
        _dbFactory.Dispose();
        _tmp.Dispose();
    }
}