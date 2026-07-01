using System.IO.Compression;
using System.Text;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Services;

namespace SpellCrafter.Tests.IntegrationTests;

/// <summary>
/// Tests that AddonInstallationService correctly rolls back partial operations
/// when failures or cancellation occur at various phases.
/// </summary>
[Collection("AddonInstallationService")] // Serialize to avoid SemaphoreSlim conflicts
public sealed class AddonInstallationServiceRollbackTests : IDisposable
{
    private readonly TempDirectoryFixture _tmp;
    private readonly FakeEsoDataConnectionFactory _dbFactory;
    private readonly AddonOperationJournalStoreAdapter _journalStore;
    private readonly IAddonDataManager _dataManager;
    private readonly IArchiveDownloader _downloader;
    private readonly AddonInstallationService _service;
    private readonly string _originalCurrentDir;

    public AddonInstallationServiceRollbackTests()
    {
        try
        {
            _originalCurrentDir = Environment.CurrentDirectory;
        }
        catch
        {
            _originalCurrentDir = Directory.GetCurrentDirectory();
        }

        _tmp = new TempDirectoryFixture();
        Environment.CurrentDirectory = _tmp.RootPath;
        AppSettings.Instance.AddonsDirectory = _tmp.AddonsPath;

        _dbFactory = new FakeEsoDataConnectionFactory();
        _journalStore = new AddonOperationJournalStoreAdapter(_dbFactory);
        _dataManager = Substitute.For<IAddonDataManager>();
        _dataManager.OnlineAddons.Returns(Array.Empty<Addon>());
        _dataManager.InstalledAddons.Returns(Array.Empty<Addon>());

        _downloader = Substitute.For<IArchiveDownloader>();
        _service = new AddonInstallationService(
            _journalStore, _dataManager, _downloader, _dbFactory);
    }

    // ========== Group 2: Install error/cancel ==========

    [Fact]
    public async Task Install_DownloaderReturnsNull_RollsBackNewInstall()
    {
        // Arrange
        var addon = CreateTestAddon("TestAddon", "", "1.0.0");
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        // Act
        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.NotNull(result.ErrorMessage);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));

        Assert.Equal(AddonState.NotInstalled, addon.State);
        _downloader.Received(1).DownloadAddonArchive(
            addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_DownloaderThrowsIOException_PreservesExceptionAndRollsBack()
    {
        // Arrange
        var addon = CreateTestAddon("TestAddon", "", "1.0.0");
        var ioException = new IOException("simulated download I/O failure");
        _downloader
            .When(x => x.DownloadAddonArchive(
                addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw ioException);

        // Act
        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.NotNull(result.Exception);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));

        Assert.Equal(AddonState.NotInstalled, addon.State);
        _downloader.Received(1).DownloadAddonArchive(
            addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_CancelAfterMarkingInstalling_RollsBackAsCanceled()
    {
        // Arrange
        var archivePath = Path.Combine(_tmp.RootPath, "valid.zip");
        FixtureArchiveBuilder.CreateValidAddonArchive("TestAddon", archivePath);

        var addon = CreateTestAddon("TestAddon", "", "1.0.0");
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(archivePath));

        using var cts = new CancellationTokenSource();

        // Wrap journal store to cancel when we reach the extraction phase
        var delegating = new DelegatingJournalStore(
            _journalStore,
            (journal, phase) =>
            {
                if (phase == AddonOperationPhase.ExtractingToStaging)
                    cts.Cancel();
            });

        var serviceWithCancel = new AddonInstallationService(
            delegating, _dataManager, _downloader, _dbFactory);

        // Act
        var result = await serviceWithCancel.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, cts.Token);

        // Assert
        Assert.False(result.Succeeded);
        Assert.True(result.WasCanceled);
        Assert.False(result.CompletedAfterCancellation);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));

        Assert.Equal(AddonState.NotInstalled, addon.State);

        // Journal should be completed as canceled rolled back
        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.DoesNotContain(incomplete, j => j.AddonName == "TestAddon");
    }

    [Fact]
    public async Task Install_UnsafeAddonName_FailsBeforeDownload()
    {
        // Arrange
        var addon = CreateTestAddon("../Evil", "", "1.0.0");

        // Act
        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.NotNull(result.ErrorMessage);

        _downloader.DidNotReceive().DownloadAddonArchive(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        var evilPath = Path.Combine(_tmp.AddonsPath, "..", "Evil");
        var resolvedPath = Path.GetFullPath(evilPath);
        Assert.False(Directory.Exists(resolvedPath));
    }

    // ========== Group 3: Replace rollback ==========

    [Fact]
    public async Task Update_ExtractionFailureBeforeBackup_KeepsExistingTarget()
    {
        // Arrange — first install a valid addon
        var addon = await InstallAddon("TestAddon");

        // Create a bad update archive (missing manifest)
        var badArchivePath = Path.Combine(_tmp.RootPath, "bad-update.zip");
        FixtureArchiveBuilder.CreateArchiveMissingManifest("TestAddon", badArchivePath);

        _downloader.ClearReceivedCalls();
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(badArchivePath));

        // Act
        var result = await _service.UpdateAsync(
            addon, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.True(Directory.Exists(targetPath));
        Assert.True(File.Exists(Path.Combine(targetPath, "TestAddon.txt")));
        Assert.True(File.Exists(Path.Combine(targetPath, "file.lua")));

        Assert.Equal(AddonState.LatestVersion, addon.State);
        Assert.Equal("1.0.0", addon.Version);
        Assert.Equal(AddonInstallationMethod.SpellCrafter, addon.InstallationMethod);
    }

    [Fact]
    public async Task Update_StagedMoveFailsAfterBackup_RestoresOriginalTarget()
    {
        // Arrange — first install a valid addon
        var addon = await InstallAddon("TestAddon");

        // Create a valid update archive
        var updateArchivePath = Path.Combine(_tmp.RootPath, "update.zip");
        CreateVersionArchive("TestAddon", "2.0", updateArchivePath);

        _downloader.ClearReceivedCalls();
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(updateArchivePath));

        // Use DelegatingJournalStore to delete staged directory after backup
        // so Directory.Move(staging, target) fails with DirectoryNotFoundException
        var delegating = new DelegatingJournalStore(
            _journalStore,
            (journal, phase) =>
            {
                if (phase == AddonOperationPhase.AfterExistingTargetBackup &&
                    journal.StagedAddonDirectory != null &&
                    Directory.Exists(journal.StagedAddonDirectory))
                    Directory.Delete(journal.StagedAddonDirectory, true);
            });

        var serviceWithFault = new AddonInstallationService(
            delegating, _dataManager, _downloader, _dbFactory);

        // Act
        var result = await serviceWithFault.UpdateAsync(
            addon, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);

        // Original target should be restored from backup
        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.True(Directory.Exists(targetPath));
        Assert.True(File.Exists(Path.Combine(targetPath, "TestAddon.txt")));
        Assert.True(File.Exists(Path.Combine(targetPath, "file.lua")));

        // Version should be rolled back
        Assert.Equal(AddonState.LatestVersion, addon.State);
        Assert.Equal("1.0.0", addon.Version);
        Assert.Equal(AddonInstallationMethod.SpellCrafter, addon.InstallationMethod);
    }

    [Fact]
    public async Task Update_BackupRestoreAlsoFails_LeavesBackupPathAndMarksInstallationError()
    {
        // Arrange — first install a valid addon
        var addon = await InstallAddon("TestAddon");

        // Create a valid update archive
        var updateArchivePath = Path.Combine(_tmp.RootPath, "update2.zip");
        CreateVersionArchive("TestAddon", "2.0", updateArchivePath);

        _downloader.ClearReceivedCalls();
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(updateArchivePath));

        // Use DelegatingJournalStore to:
        //  1. Delete staged directory (staged→target move fails)
        //  2. Create a FILE at the target path (backup→target restore fails
        //     because Directory.Move cannot replace a file with a directory)
        var delegating = new DelegatingJournalStore(
            _journalStore,
            (journal, phase) =>
            {
                if (phase == AddonOperationPhase.AfterExistingTargetBackup)
                {
                    // Delete staged to cause staged→target move to fail
                    if (journal.StagedAddonDirectory != null &&
                        Directory.Exists(journal.StagedAddonDirectory))
                        Directory.Delete(journal.StagedAddonDirectory, true);

                    // Create a FILE at the target path (not a directory).
                    // Directory.Exists returns false for files, so the restore
                    // condition passes, but Directory.Move(backup_dir, file)
                    // fails because a directory cannot replace an existing file.
                    if (journal.TargetAddonDirectory != null)
                        File.WriteAllText(
                            journal.TargetAddonDirectory,
                            "file blocks move");
                }
            });

        var serviceWithFault = new AddonInstallationService(
            delegating, _dataManager, _downloader, _dbFactory);

        // Act
        var result = await serviceWithFault.UpdateAsync(
            addon, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.NotNull(result.BackupPath);

        // Backup directory should still exist
        Assert.True(Directory.Exists(result.BackupPath));
        Assert.True(File.Exists(Path.Combine(result.BackupPath!, "TestAddon.txt")));

        // Addon should be in error state
        Assert.Equal(AddonState.InstallationError, addon.State);

        // Target path still has the blocking file
        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.True(File.Exists(targetPath));
        Assert.Equal("file blocks move", File.ReadAllText(targetPath));

        // Operation root should not be cleaned (preserved for recovery)
        var opsRoot = Path.Combine(_tmp.RootPath, ".SpellCrafterOperations");
        Assert.True(Directory.Exists(opsRoot));
    }

    [Fact]
    public async Task Reinstall_ExtractionFailsBeforeBackup_KeepsExistingTarget()
    {
        // Arrange — first install a valid addon
        var addon = await InstallAddon("TestAddon");

        // Create bad reinstall archive (missing manifest)
        var badArchivePath = Path.Combine(_tmp.RootPath, "bad-reinstall.zip");
        FixtureArchiveBuilder.CreateArchiveMissingManifest("TestAddon", badArchivePath);

        _downloader.ClearReceivedCalls();
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(badArchivePath));

        // Act
        var result = await _service.ReinstallAsync(
            addon, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.True(Directory.Exists(targetPath));
        Assert.True(File.Exists(Path.Combine(targetPath, "TestAddon.txt")));

        Assert.Equal(AddonState.LatestVersion, addon.State);
        Assert.Equal("1.0.0", addon.Version);
        Assert.Equal(AddonInstallationMethod.SpellCrafter, addon.InstallationMethod);
    }

    // ========== Group 4: Delete rollback ==========

    [Fact]
    public async Task Delete_CancellationAfterBackup_RestoresTarget()
    {
        // Arrange — first install a valid addon
        var addon = await InstallAddon("TestAddon");

        using var cts = new CancellationTokenSource();

        // Wrap journal store to cancel after backup
        var delegating = new DelegatingJournalStore(
            _journalStore,
            (journal, phase) =>
            {
                if (phase == AddonOperationPhase.AfterTargetBackupForDelete)
                    cts.Cancel();
            });

        var serviceWithCancel = new AddonInstallationService(
            delegating, _dataManager, _downloader, _dbFactory);

        // Act
        var result = await serviceWithCancel.DeleteAsync(addon, null, cts.Token);

        // Assert — after backup, cancellation triggers general exception path
        // which should restore the backup
        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.True(Directory.Exists(targetPath));
        Assert.True(File.Exists(Path.Combine(targetPath, "TestAddon.txt")));

        // Addon metadata should be preserved (backup restored)
        Assert.Equal(AddonState.LatestVersion, addon.State);
        Assert.Equal("1.0.0", addon.Version);
    }

    [Fact]
    public async Task Delete_RemoveLocalAddonThrowsAfterBackup_RestoresTarget()
    {
        // Arrange — first install a valid addon
        var addon = await InstallAddon("TestAddon");

        // Make RemoveLocalAddon throw
        _dataManager
            .When(x => x.RemoveLocalAddon(addon))
            .Do(_ => throw new InvalidOperationException("simulated database delete failure"));

        // Act
        var result = await _service.DeleteAsync(addon, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.NotNull(result.Exception);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.True(Directory.Exists(targetPath));
        Assert.True(File.Exists(Path.Combine(targetPath, "TestAddon.txt")));

        Assert.Equal(AddonState.LatestVersion, addon.State);
        Assert.Equal("1.0.0", addon.Version);
    }

    [Fact]
    public async Task Delete_RemoveLocalAddonThrows_RestoresBackup()
    {
        // Arrange — first install a valid addon
        var addon = await InstallAddon("TestAddon");

        // Make RemoveLocalAddon throw during delete
        _dataManager
            .When(x => x.RemoveLocalAddon(addon))
            .Do(_ => throw new InvalidOperationException("simulated database delete failure"));

        // Act
        var result = await _service.DeleteAsync(addon, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.NotNull(result.Exception);

        // Backup was restored — addon remains in LatestVersion state
        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.True(Directory.Exists(targetPath));
        Assert.True(File.Exists(Path.Combine(targetPath, "TestAddon.txt")));

        Assert.Equal(AddonState.LatestVersion, addon.State);
        Assert.Equal("1.0.0", addon.Version);
    }

    [Fact]
    public async Task Delete_UnsafeAddonName_FailsBeforeFilesystemAction()
    {
        // Arrange
        var addon = new Addon
        {
            CommonAddonId = 102,
            UniqueId = 1002,
            Name = "../Evil",
            Title = "Evil",
            Version = "1.0.0",
            DisplayedVersion = "1.0.0",
            State = AddonState.LatestVersion,
            InstallationMethod = AddonInstallationMethod.SpellCrafter,
            LocalAddonId = 999
        };

        // Record sentinel to ensure it's not touched
        var sentinelPath = Path.Combine(_tmp.RootPath, "sentinel.txt");
        File.WriteAllText(sentinelPath, "do not delete");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(addon, null, default));
        Assert.Contains("folder name", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Sentinel should be untouched
        Assert.True(File.Exists(sentinelPath));
        Assert.Equal("do not delete", File.ReadAllText(sentinelPath));
    }

    public void Dispose()
    {
        Environment.CurrentDirectory = _originalCurrentDir;
        _tmp.Dispose();
        _dbFactory.Dispose();
    }

    // ---- Helpers ----

    private static Addon CreateTestAddon(
        string name, string version, string latestVersion)
    {
        return new Addon
        {
            CommonAddonId = 101,
            UniqueId = 1001,
            Name = name,
            Title = name,
            Version = version,
            DisplayedVersion = version,
            LatestVersion = latestVersion,
            DisplayedLatestVersion = latestVersion,
            State = string.IsNullOrEmpty(version)
                ? AddonState.NotInstalled
                : AddonState.LatestVersion,
            InstallationMethod = AddonInstallationMethod.Other,
            LocalAddonId = string.IsNullOrEmpty(version) ? null : 501
        };
    }

    /// <summary>
    /// Installs a valid addon and returns it with state set to LatestVersion.
    /// </summary>
    private async Task<Addon> InstallAddon(string name)
    {
        var archivePath = Path.Combine(_tmp.RootPath, $"install-{name}.zip");
        FixtureArchiveBuilder.CreateValidAddonArchive(name, archivePath);

        var addon = CreateTestAddon(name, "", "1.0.0");
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(archivePath));

        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.True(result.Succeeded, $"Installation failed: {result.ErrorMessage}");
        Assert.Equal(AddonState.LatestVersion, addon.State);
        Assert.Equal("1.0.0", addon.Version);
        addon.InstallationMethod = AddonInstallationMethod.SpellCrafter;

        return addon;
    }

    /// <summary>
    /// Creates a ZIP archive with a specific version for the given addon name.
    /// </summary>
    private static void CreateVersionArchive(string addonName, string version, string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var manifest = archive.CreateEntry($"{addonName}/{addonName}.txt");
        using (var writer = new StreamWriter(manifest.Open(), Encoding.UTF8))
        {
            writer.WriteLine($"## {addonName}");
            writer.WriteLine($"## Version: {version}");
        }

        var entry = archive.CreateEntry($"{addonName}/file.lua");
        using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- updated file");
        }
    }
}