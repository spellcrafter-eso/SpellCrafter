using System.IO.Compression;
using System.Text;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Services;

namespace SpellCrafter.Tests.IntegrationTests;

/// <summary>
/// Tests that AddonInstallationService correctly rejects archives with unsafe or
/// invalid structures, never writing outside the expected target directory.
/// </summary>
[Collection("EnvironmentStateCollection")] // Serialize with other env-modifying tests
public sealed class AddonInstallationServiceArchiveSafetyTests : IDisposable
{
    private readonly TempDirectoryFixture _tmp;
    private readonly FakeEsoDataConnectionFactory _dbFactory;
    private readonly AddonOperationJournalStoreAdapter _journalStore;
    private readonly IAddonDataManager _dataManager;
    private readonly IArchiveDownloader _downloader;
    private readonly AddonInstallationService _service;
    private readonly string _originalCurrentDir;

    public AddonInstallationServiceArchiveSafetyTests()
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

    [Fact]
    public async Task Install_RejectsPathTraversalEntry()
    {
        // Arrange
        var archivePath = Path.Combine(_tmp.RootPath, "traversal.zip");
        FixtureArchiveBuilder.CreateArchiveWithAddonTraversalEntry("TestAddon", archivePath);

        var addon = CreateTestAddon("TestAddon");
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(archivePath));

        // Act
        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));
        Assert.False(File.Exists(Path.Combine(_tmp.AddonsPath, "evil.lua")));

        Assert.Equal(AddonState.NotInstalled, addon.State);
        Assert.Equal(AddonInstallationMethod.Other, addon.InstallationMethod);
        _downloader.Received(1).DownloadAddonArchive(
            addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Journal may remain incomplete (requires recovery) — we don't assert
        // completion since the operation failed before committing.
    }

    [Fact]
    public async Task Install_RejectsRootedUnixEntry()
    {
        // Arrange
        var archivePath = Path.Combine(_tmp.RootPath, "rooted-unix.zip");
        FixtureArchiveBuilder.CreateArchiveWithRootedUnixEntry("TestAddon", archivePath);

        var addon = CreateTestAddon("TestAddon");
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(archivePath));

        // Act
        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));

        Assert.Equal(AddonState.NotInstalled, addon.State);
        _downloader.Received(1).DownloadAddonArchive(
            addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_RejectsWindowsRootedEntry()
    {
        // Arrange
        var archivePath = Path.Combine(_tmp.RootPath, "windows-rooted.zip");
        FixtureArchiveBuilder.CreateArchiveWithWindowsRootedEntry("TestAddon", archivePath);

        var addon = CreateTestAddon("TestAddon");
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(archivePath));

        // Act
        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        // No target directory created

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));

        Assert.Equal(AddonState.NotInstalled, addon.State);
        _downloader.Received(1).DownloadAddonArchive(
            addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_RejectsWrongTopLevelFolder()
    {
        // Arrange
        var archivePath = Path.Combine(_tmp.RootPath, "wrong-top-level.zip");
        FixtureArchiveBuilder.CreateWrongAddonArchive(archivePath);

        var addon = CreateTestAddon("TestAddon");
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(archivePath));

        // Act
        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.NotNull(result.Exception);
        // No target created because archive validation rejected it

        Assert.False(Directory.Exists(Path.Combine(_tmp.AddonsPath, "TestAddon")));
        Assert.False(Directory.Exists(Path.Combine(_tmp.AddonsPath, "OtherAddon")));

        Assert.Equal(AddonState.NotInstalled, addon.State);
        _downloader.Received(1).DownloadAddonArchive(
            addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_RejectsMissingManifest()
    {
        // Arrange
        var archivePath = Path.Combine(_tmp.RootPath, "missing-manifest.zip");
        FixtureArchiveBuilder.CreateArchiveMissingManifest("TestAddon", archivePath);

        var addon = CreateTestAddon("TestAddon");
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(archivePath));

        // Act
        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.NotNull(result.Exception);
        // No target created because manifest was missing

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));

        Assert.Equal(AddonState.NotInstalled, addon.State);
        _downloader.Received(1).DownloadAddonArchive(
            addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_RejectsFilesAtArchiveRoot()
    {
        // Arrange
        var archivePath = Path.Combine(_tmp.RootPath, "files-at-root.zip");
        FixtureArchiveBuilder.CreateArchiveFilesAtRoot("TestAddon", archivePath);

        var addon = CreateTestAddon("TestAddon");
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(archivePath));

        // Act
        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.NotNull(result.Exception);
        // No target created because archive validation rejected it

        Assert.False(Directory.Exists(Path.Combine(_tmp.AddonsPath, "TestAddon")));
        Assert.False(File.Exists(Path.Combine(_tmp.AddonsPath, "TestAddon.txt")));
        Assert.False(File.Exists(Path.Combine(_tmp.AddonsPath, "file.lua")));

        Assert.Equal(AddonState.NotInstalled, addon.State);
        _downloader.Received(1).DownloadAddonArchive(
            addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_RejectsDuplicateCaseInsensitivePaths()
    {
        // Arrange
        var archivePath = Path.Combine(_tmp.RootPath, "case-duplicate.zip");
        FixtureArchiveBuilder.CreateArchiveWithCaseDuplicatePaths("TestAddon", archivePath);

        var addon = CreateTestAddon("TestAddon");
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(archivePath));

        // Act
        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.NotNull(result.Exception);
        // No target created because archive had duplicate paths

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));

        Assert.Equal(AddonState.NotInstalled, addon.State);
        _downloader.Received(1).DownloadAddonArchive(
            addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_RejectsArchiveWithNoFiles()
    {
        // Arrange
        var archivePath = Path.Combine(_tmp.RootPath, "empty.zip");
        FixtureArchiveBuilder.CreateEmptyArchive(archivePath);

        var addon = CreateTestAddon("TestAddon");
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(archivePath));

        // Act
        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.NotNull(result.Exception);
        // No target created because archive had no files

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));

        Assert.Equal(AddonState.NotInstalled, addon.State);
        _downloader.Received(1).DownloadAddonArchive(
            addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_RejectsArchiveWithOnlyDirectories()
    {
        // Arrange
        var archivePath = Path.Combine(_tmp.RootPath, "only-dirs.zip");
        FixtureArchiveBuilder.CreateArchiveOnlyDirectories("TestAddon", archivePath);

        var addon = CreateTestAddon("TestAddon");
        _downloader
            .DownloadAddonArchive(addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(archivePath));

        // Act
        var result = await _service.InstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.NotNull(result.Exception);
        // No target created because archive had no files

        var targetPath = Path.Combine(_tmp.AddonsPath, "TestAddon");
        Assert.False(Directory.Exists(targetPath));

        Assert.Equal(AddonState.NotInstalled, addon.State);
        _downloader.Received(1).DownloadAddonArchive(
            addon.UniqueId!.Value, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        Environment.CurrentDirectory = _originalCurrentDir;
        _tmp.Dispose();
        _dbFactory.Dispose();
    }

    private static Addon CreateTestAddon(string name)
    {
        return new Addon
        {
            CommonAddonId = 101,
            UniqueId = 1001,
            Name = name,
            Title = name,
            Version = "",
            DisplayedVersion = "",
            LatestVersion = "1.0.0",
            DisplayedLatestVersion = "1.0.0",
            State = AddonState.NotInstalled,
            InstallationMethod = AddonInstallationMethod.Other,
            LocalAddonId = null
        };
    }
}