using System.IO.Compression;
using System.Text;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;
using SpellCrafter.Services;
using SpellCrafter.Tests.TestInfrastructure;

namespace SpellCrafter.Tests.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="AddonInstallationService"/>.
/// Each test creates:
///   - A temp directory as the addons root
///   - A temp SQLite database for the journal
///   - A fixture archive for the fake downloader
///   - NSubstitute mocks for IAddonOperationJournalStore and IAddonDataManager
///
/// The static AppSettings.Instance.AddonsDirectory is set per test via Environment.CurrentDirectory
/// pointing at a temp directory.
/// </summary>
[Collection("EnvironmentStateCollection")] // Serialize with other env-modifying tests
public sealed class AddonInstallationServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _tmp;
    private readonly FakeEsoDataConnectionFactory _dbFactory;
    private readonly FakeEsoDataConnectionFactory _realDbFactory;
    private readonly IAddonOperationJournalStore _journalStore;
    private readonly IAddonDataManager _dataManager;
    private readonly IArchiveDownloader _downloader;
    private readonly AddonInstallationService _service;
    private readonly string _fixtureArchivePath;
    private readonly string _originalCurrentDir;
    private readonly Addon _addon;

    public AddonInstallationServiceTests()
    {
        // Save current directory to restore later.
        // Reading Environment.CurrentDirectory may fail if a previous test
        // left it pointing to a deleted directory.
        try
        {
            _originalCurrentDir = Environment.CurrentDirectory;
        }
        catch
        {
            _originalCurrentDir = Directory.GetCurrentDirectory();
        }

        // Create temp directory for the addon and isolate AppSettings
        _tmp = new TempDirectoryFixture();
        Environment.CurrentDirectory = _tmp.RootPath;

        // Set AddonsDirectory to our temp AddOns folder
        AppSettings.Instance.AddonsDirectory = _tmp.AddonsPath;

        // Real DB factory for journal store (needed by recovery)
        _realDbFactory = new FakeEsoDataConnectionFactory();

        // Create the journal store adapter backed by real DB
        _journalStore = new AddonOperationJournalStoreAdapter(_realDbFactory);

        // Mock data manager
        _dataManager = Substitute.For<IAddonDataManager>();

        // Create fixture archive
        _fixtureArchivePath = Path.Combine(_tmp.RootPath, "fixture.zip");
        CreateValidAddonArchive("TestAddon", _fixtureArchivePath);

        // Fake downloader returns the fixture
        _downloader = new FakeArchiveDownloader(_fixtureArchivePath);

        // DB factory for the service (real SQLite)
        _dbFactory = new FakeEsoDataConnectionFactory();

        _service = new AddonInstallationService(
            _journalStore,
            _dataManager,
            _downloader,
            _dbFactory);

        // Create the addon with the service attached
        _addon = new Addon(_service)
        {
            CommonAddonId = 100,
            UniqueId = 456,
            Name = "TestAddon",
            Title = "Test Addon",
            Version = "",
            DisplayedVersion = "",
            LatestVersion = "1.0",
            DisplayedLatestVersion = "1.0.0",
            State = AddonState.NotInstalled
        };
    }

    [Fact]
    public async Task Install_Success()
    {
        var result = await _service.InstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(AddonState.LatestVersion, _addon.State);
        Assert.Equal("1.0", _addon.Version);

        // Target addon directory should exist
        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.True(Directory.Exists(targetPath));
        Assert.True(File.Exists(Path.Combine(targetPath, "TestAddon.txt")));
        Assert.True(File.Exists(Path.Combine(targetPath, "file.lua")));

        // Data manager should have been called to insert:
        // 1. MarkingLocalAddonInstalling (change to Installing state)
        // 2. BeforeDatabaseCommit (marking as LatestVersion)
        // 3. Finally block (ensure state persisted)
        _dataManager.Received(3).InsertOrUpdateLocalAddon(_addon);
        // Verify RemoveLocalAddon was NOT called
        _dataManager.DidNotReceive().RemoveLocalAddon(Arg.Any<Addon>());

        // Journal should have been committed
        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.Empty(incomplete);
    }

    [Fact]
    public async Task Install_Idempotent_TargetExists()
    {
        // First install
        await _service.InstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);
        Assert.True(_addon.State == AddonState.LatestVersion);

        // Second install should fail because target exists
        var result = await _service.InstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Install_NoUniqueId_Fails()
    {
        var addon = new Addon(_service)
        {
            CommonAddonId = 101,
            Name = "NoIdAddon",
            UniqueId = null,
            State = AddonState.NotInstalled
        };

        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.False(result.Succeeded);
        Assert.Contains("no unique online id", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Install_CanceledBeforeCommit_RollsBack()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var result = await _service.InstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, cts.Token);

        Assert.True(result.WasCanceled);
        Assert.False(result.Succeeded);

        // Target should not exist
        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));

        // State should be rolled back to NotInstalled
        Assert.Equal(AddonState.NotInstalled, _addon.State);
    }

    [Fact]
    public async Task Delete_Success()
    {
        // First install
        await _service.InstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Reset mock call counts
        _dataManager.ClearReceivedCalls();

        var result = await _service.DeleteAsync(_addon, null, default);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(AddonState.NotInstalled, _addon.State);

        // Target should not exist
        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));

        // The operation root is cleaned up after successful delete
        // (journal is complete, so TryDeleteDirectoryIfExists removes it).
        // The parent .SpellCrafterOperations directory survives.
        var backupDir = Directory.GetParent(_tmp.AddonsPath)!.FullName;
        var opsDir = Path.Combine(backupDir, ".SpellCrafterOperations");
        Assert.True(Directory.Exists(opsDir));
        // The specific operation subdirectory should be gone
        Assert.Empty(Directory.GetDirectories(opsDir));

        _dataManager.Received(1).RemoveLocalAddon(_addon);
    }

    [Fact]
    public async Task Delete_NotInstalled_FailsGracefully()
    {
        var addon = new Addon(_service)
        {
            CommonAddonId = 102,
            Name = "NonExistent",
            UniqueId = 789,
            State = AddonState.NotInstalled
        };

        var result = await _service.DeleteAsync(addon, null, default);

        // Should succeed since there's nothing to delete
        Assert.True(result.Succeeded, result.ErrorMessage);
    }

    [Fact]
    public async Task Update_Success()
    {
        // Install version 1.0
        await _service.InstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Now set a newer version
        _addon.LatestVersion = "2.0";
        _addon.DisplayedLatestVersion = "2.0.0";

        _dataManager.ClearReceivedCalls();

        var result = await _service.UpdateAsync(_addon, false, null, default);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(AddonState.LatestVersion, _addon.State);
        Assert.Equal("2.0", _addon.Version);

        // Target should still exist with updated content
        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.True(Directory.Exists(targetPath));
    }

    [Fact]
    public async Task Update_AlreadyLatest_NoOp()
    {
        await _service.InstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Same version
        _addon.LatestVersion = "1.0";
        _addon.DisplayedLatestVersion = "1.0.0";

        _dataManager.ClearReceivedCalls();

        var result = await _service.UpdateAsync(_addon, false, null, default);

        Assert.True(result.Succeeded, result.ErrorMessage);
    }

    [Fact]
    public async Task Reinstall_Success()
    {
        await _service.InstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        _dataManager.ClearReceivedCalls();

        var result = await _service.ReinstallAsync(_addon, false, null, default);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(AddonState.LatestVersion, _addon.State);

        // Target should exist
        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.True(Directory.Exists(targetPath));
    }

    [Fact]
    public async Task Progress_IsReported()
    {
        var progressReports = new List<InstallProgress>();
        var progress = new Progress<InstallProgress>(r => progressReports.Add(r));

        await _service.InstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, progress, default);

        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, p => p.Stage == InstallProgressStage.Preparing);
        Assert.Contains(progressReports, p => p.Stage == InstallProgressStage.Downloading);
        Assert.Contains(progressReports, p => p.Stage == InstallProgressStage.Extracting);
        Assert.Contains(progressReports, p => p.Stage == InstallProgressStage.Moving);
        Assert.Contains(progressReports, p => p.Stage == InstallProgressStage.Committing);
        Assert.Contains(progressReports, p => p.Stage == InstallProgressStage.Completed);
    }

    [Fact]
    public async Task Install_CorruptArchive_RollsBack()
    {
        // Arrange — create a malformed "archive" (plain text file)
        var badArchivePath = Path.Combine(_tmp.RootPath, "bad.zip");
        File.WriteAllText(badArchivePath, "this is not a zip archive");
        var badDownloader = new FakeArchiveDownloader(badArchivePath);

        var localService = new AddonInstallationService(
            _journalStore, _dataManager, badDownloader, _dbFactory);

        var addon = new Addon(localService)
        {
            CommonAddonId = 200,
            UniqueId = 2000,
            Name = "TestAddon",
            Title = "Test Addon",
            Version = "",
            DisplayedVersion = "",
            LatestVersion = "1.0",
            DisplayedLatestVersion = "1.0.0",
            State = AddonState.NotInstalled
        };

        // Act
        var result = await localService.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.Equal(AddonState.NotInstalled, addon.State);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));

        // Journal should remain incomplete (not rolled back, left for recovery)
        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.Contains(incomplete, j => j.AddonName == "TestAddon");
    }

    [Fact]
    public async Task Update_CorruptArchive_RestoresOriginal()
    {
        // First install
        await _service.InstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);
        Assert.True(_addon.State == AddonState.LatestVersion);

        // Capture original content
        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        var originalFile = Path.Combine(targetPath, "TestAddon.txt");
        var originalContent = File.ReadAllText(originalFile);

        // Set newer version
        _addon.LatestVersion = "2.0";
        _addon.DisplayedLatestVersion = "2.0.0";

        // Create malformed archive
        var badArchivePath = Path.Combine(_tmp.RootPath, "bad-update.zip");
        File.WriteAllText(badArchivePath, "not a zip");
        var badDownloader = new FakeArchiveDownloader(badArchivePath);

        var localService = new AddonInstallationService(
            _journalStore, _dataManager, badDownloader, _dbFactory);

        // Act
        var result = await localService.UpdateAsync(_addon, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);

        // Original target should still exist with original content
        Assert.True(Directory.Exists(targetPath));
        Assert.Equal(originalContent, File.ReadAllText(originalFile));

        Assert.Equal(AddonState.LatestVersion, _addon.State);
        Assert.Equal("1.0", _addon.Version);
    }

    [Fact]
    public async Task Delete_RemoveLocalAddonThrowsAndBackupMissing_FailsGracefully()
    {
        // First install
        await _service.InstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);
        Assert.True(_addon.State == AddonState.LatestVersion);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");

        // Make RemoveLocalAddon throw AND remove the backup/operations directory
        _dataManager
            .When(x => x.RemoveLocalAddon(_addon))
            .Do(_ =>
            {
                // Delete the backup directory to simulate a double failure
                var opsDir = Path.Combine(Directory.GetParent(_tmp.AddonsPath)!.FullName, ".SpellCrafterOperations");
                if (Directory.Exists(opsDir))
                    Directory.Delete(opsDir, true);
                throw new InvalidOperationException("simulated db failure");
            });

        // Act
        var result = await _service.DeleteAsync(_addon, null, default);

        // Assert — code should handle gracefully (restore metadata, no crash)
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);

        // Target was moved to backup (then backup was deleted), so target is gone
        Assert.False(Directory.Exists(targetPath));

        // Addon state should be restored by the rollback
        Assert.Equal(AddonState.LatestVersion, _addon.State);
        Assert.Equal("1.0", _addon.Version);
    }

    public void Dispose()
    {
        Environment.CurrentDirectory = _originalCurrentDir;
        _tmp.Dispose();
        _dbFactory.Dispose();
        _realDbFactory.Dispose();
    }

    private static void CreateValidAddonArchive(string addonName, string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var entry = archive.CreateEntry($"{addonName}/{addonName}.txt");
        using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
        {
            writer.WriteLine($"## {addonName}\n## Version: 1.0");
        }

        entry = archive.CreateEntry($"{addonName}/file.lua");
        using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- test lua file");
        }
    }
}