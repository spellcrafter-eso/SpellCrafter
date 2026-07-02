using System.IO.Compression;
using System.Text;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;
using SpellCrafter.Services;
using SpellCrafter.Tests.TestInfrastructure;

namespace SpellCrafter.Tests.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="SingleAddonInstallationWorker"/>.
/// Each test creates:
///   - A temp directory as the addons root
///   - A temp SQLite database for the journal
///   - A fixture archive for the fake downloader
///   - NSubstitute mocks for IAddonOperationJournalStore and IAddonDataManager
/// </summary>
[Collection("EnvironmentStateCollection")]
public sealed class SingleAddonInstallationWorkerTests : IDisposable
{
    private readonly TempDirectoryFixture _tmp;
    private readonly FakeEsoDataConnectionFactory _realDbFactory;
    private readonly IAddonOperationJournalStore _journalStore;
    private readonly IAddonDataManager _dataManager;
    private readonly IArchiveDownloader _downloader;
    private readonly ISingleAddonInstallationWorker _worker;
    private readonly string _fixtureArchivePath;
    private readonly string _originalCurrentDir;
    private readonly Addon _addon;

    public SingleAddonInstallationWorkerTests()
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

        _realDbFactory = new FakeEsoDataConnectionFactory();
        _journalStore = new AddonOperationJournalStoreAdapter(_realDbFactory);
        _dataManager = Substitute.For<IAddonDataManager>();

        _fixtureArchivePath = Path.Combine(_tmp.RootPath, "fixture.zip");
        CreateValidAddonArchive("TestAddon", _fixtureArchivePath);

        _downloader = new FakeArchiveDownloader(_fixtureArchivePath);

        _worker = new SingleAddonInstallationWorker(
            _journalStore, _dataManager, _downloader, _realDbFactory);

        _addon = new Addon
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

    public void Dispose()
    {
        try
        {
            Environment.CurrentDirectory = _originalCurrentDir;
        }
        catch
        {
            // Best-effort
        }

        _tmp.Dispose();
        _realDbFactory.Dispose();
    }

    [Fact]
    public async Task InstallOne_Success()
    {
        var result = await _worker.InstallOneAsync(
            _addon, AddonInstallationMethod.SpellCrafter, null, default);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(AddonState.LatestVersion, _addon.State);
        Assert.Equal("1.0", _addon.Version);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.True(Directory.Exists(targetPath));
        Assert.True(File.Exists(Path.Combine(targetPath, "TestAddon.txt")));
        Assert.True(File.Exists(Path.Combine(targetPath, "file.lua")));

        _dataManager.Received(3).InsertOrUpdateLocalAddon(_addon);
        _dataManager.DidNotReceive().RemoveLocalAddon(Arg.Any<Addon>());

        var incomplete = _journalStore.GetIncompleteOperations();
        Assert.Empty(incomplete);
    }

    [Fact]
    public async Task InstallOne_NoUniqueId_Fails()
    {
        var addon = new Addon
        {
            CommonAddonId = 101,
            Name = "NoIdAddon",
            UniqueId = null,
            State = AddonState.NotInstalled
        };

        var result = await _worker.InstallOneAsync(
            addon, AddonInstallationMethod.SpellCrafter, null, default);

        Assert.False(result.Succeeded);
        Assert.Contains("no unique online id", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallOne_CanceledBeforeCommit_RollsBack()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _worker.InstallOneAsync(
            _addon, AddonInstallationMethod.SpellCrafter, null, cts.Token);

        Assert.True(result.WasCanceled);
        Assert.False(result.Succeeded);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));
        Assert.Equal(AddonState.NotInstalled, _addon.State);
    }

    [Fact]
    public async Task InstallOne_Idempotent_TargetExists()
    {
        await _worker.InstallOneAsync(
            _addon, AddonInstallationMethod.SpellCrafter, null, default);
        Assert.True(_addon.State == AddonState.LatestVersion);

        var result = await _worker.InstallOneAsync(
            _addon, AddonInstallationMethod.SpellCrafter, null, default);

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplaceOne_Update_Success()
    {
        // First install
        await _worker.InstallOneAsync(
            _addon, AddonInstallationMethod.SpellCrafter, null, default);
        Assert.True(_addon.State == AddonState.LatestVersion);

        _addon.LatestVersion = "2.0";
        _addon.DisplayedLatestVersion = "2.0.0";
        _addon.State = AddonState.Outdated;

        var result = await _worker.ReplaceOneAsync(
            _addon, AddonOperationType.Update, AddonInstallationMethod.SpellCrafter, null, default);

        Assert.True(result.Succeeded, result.ErrorMessage);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.True(Directory.Exists(targetPath));
    }

    [Fact]
    public async Task DeleteOne_Success()
    {
        await _worker.InstallOneAsync(
            _addon, AddonInstallationMethod.SpellCrafter, null, default);
        Assert.True(_addon.State == AddonState.LatestVersion);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.True(Directory.Exists(targetPath));

        var result = _worker.DeleteOne(_addon, null, default);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(AddonState.NotInstalled, _addon.State);
        Assert.False(Directory.Exists(targetPath));
    }

    [Fact]
    public async Task DeleteOne_NotInstalled_RemovesStaleRecord()
    {
        // An addon that has a local DB entry but is marked NotInstalled
        // should still be cleaned up (stale record removal).
        var addon = new Addon
        {
            LocalAddonId = 500,
            CommonAddonId = 999,
            Name = "Nonexistent",
            State = AddonState.NotInstalled
        };

        var result = _worker.DeleteOne(addon, null, default);

        Assert.True(result.Succeeded);
        Assert.Equal(AddonState.NotInstalled, addon.State);
        Assert.Null(addon.LocalAddonId);
    }

    private static void CreateValidAddonArchive(string addonName, string archivePath)
    {
        using var stream = new FileStream(archivePath, FileMode.CreateNew);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var manifestEntry = archive.CreateEntry($"{addonName}/{addonName}.txt");
        using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
        {
            writer.WriteLine("## TestManifest");
            writer.WriteLine($"name: {addonName}");
            writer.WriteLine("version: 1.0");
        }

        var luaEntry = archive.CreateEntry($"{addonName}/file.lua");
        using (var writer = new StreamWriter(luaEntry.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- Test Lua file");
        }
    }
}