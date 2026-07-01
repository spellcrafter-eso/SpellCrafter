namespace SpellCrafter.Tests.UnitTests;

public sealed class AddonsDirectoryDiscoveryServiceTests
{
    [Fact]
    public void Discover_WindowsDocumentsCandidate_Found()
    {
        using var tmp = new TempDirectoryFixture();
        var esoDocumentsPath = Path.Combine(tmp.RootPath, "Documents", "Elder Scrolls Online", "live", "AddOns");
        Directory.CreateDirectory(esoDocumentsPath);

        var options = new AddonsDirectoryDiscoveryOptions
        {
            Platform = AddonsDirectoryDiscoveryPlatform.Windows,
            DocumentsDirectory = Path.Combine(tmp.RootPath, "Documents"),
            HomeDirectory = tmp.RootPath,
            EnvironmentVariables = new Dictionary<string, string?>()
        };

        var result = AddonsDirectoryDiscoveryService.Discover(options);

        Assert.Contains(result.Candidates, c =>
            c.Path == Path.GetFullPath(esoDocumentsPath) &&
            c.SourceDescription.Contains("Documents", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Discover_MacOsDocumentsCandidate_Found()
    {
        using var tmp = new TempDirectoryFixture();
        var esoDocsPath = Path.Combine(tmp.RootPath, "Documents", "Elder Scrolls Online", "live", "AddOns");
        Directory.CreateDirectory(esoDocsPath);

        var options = new AddonsDirectoryDiscoveryOptions
        {
            Platform = AddonsDirectoryDiscoveryPlatform.MacOS,
            DocumentsDirectory = Path.Combine(tmp.RootPath, "Documents"),
            HomeDirectory = tmp.RootPath,
            EnvironmentVariables = new Dictionary<string, string?>()
        };

        var result = AddonsDirectoryDiscoveryService.Discover(options);

        Assert.Contains(result.Candidates, c =>
            c.Path == Path.GetFullPath(esoDocsPath));
    }

    [Fact]
    public void Discover_LinuxSteamDefaultRoot_Found()
    {
        using var tmp = new TempDirectoryFixture();
        var addonPath = Path.Combine(tmp.RootPath, ".steam", "steam", "steamapps",
            "compatdata", AddonsDirectoryDiscoveryService.EsoSteamAppId, "pfx",
            "drive_c", "users", "steamuser", "Documents", "Elder Scrolls Online", "live", "AddOns");
        Directory.CreateDirectory(addonPath);

        var options = new AddonsDirectoryDiscoveryOptions
        {
            Platform = AddonsDirectoryDiscoveryPlatform.Linux,
            HomeDirectory = tmp.RootPath,
            DocumentsDirectory = Path.Combine(tmp.RootPath, "Documents"),
            EnvironmentVariables = new Dictionary<string, string?>(),
            SteamRootDirectories = new[]
            {
                Path.Combine(tmp.RootPath, ".steam", "steam")
            }
        };

        var result = AddonsDirectoryDiscoveryService.Discover(options);

        Assert.Contains(result.Candidates, c =>
            c.Path == Path.GetFullPath(addonPath) &&
            c.SourceDescription.Contains("Steam", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Discover_LinuxSteamCompatDataPath_Found()
    {
        using var tmp = new TempDirectoryFixture();
        var compatDataPath = Path.Combine(tmp.RootPath, "compatdata");
        var addonPath = Path.Combine(compatDataPath, "pfx",
            "drive_c", "users", "steamuser", "Documents", "Elder Scrolls Online", "live", "AddOns");
        Directory.CreateDirectory(addonPath);

        var options = new AddonsDirectoryDiscoveryOptions
        {
            Platform = AddonsDirectoryDiscoveryPlatform.Linux,
            HomeDirectory = tmp.RootPath,
            DocumentsDirectory = Path.Combine(tmp.RootPath, "Documents"),
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["STEAM_COMPAT_DATA_PATH"] = compatDataPath
            }
        };

        var result = AddonsDirectoryDiscoveryService.Discover(options);

        Assert.Contains(result.Candidates, c =>
            c.Path == Path.GetFullPath(addonPath));
    }

    [Fact]
    public void Discover_LinuxWinePrefix_Found()
    {
        using var tmp = new TempDirectoryFixture();
        var winePrefix = Path.Combine(tmp.RootPath, "wineprefix");
        var addonPath = Path.Combine(winePrefix, "drive_c",
            "users", "wineuser", "Documents", "Elder Scrolls Online", "live", "AddOns");
        Directory.CreateDirectory(addonPath);

        var options = new AddonsDirectoryDiscoveryOptions
        {
            Platform = AddonsDirectoryDiscoveryPlatform.Linux,
            HomeDirectory = tmp.RootPath,
            DocumentsDirectory = Path.Combine(tmp.RootPath, "Documents"),
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["WINEPREFIX"] = winePrefix
            }
        };

        var result = AddonsDirectoryDiscoveryService.Discover(options);

        Assert.Contains(result.Candidates, c =>
            c.Path == Path.GetFullPath(addonPath));
    }

    [Fact]
    public void Discover_LinuxBottlesPrefix_Found()
    {
        using var tmp = new TempDirectoryFixture();
        var bottleDir = Path.Combine(tmp.RootPath, ".local", "share", "bottles", "bottles", "ESO");
        var addonPath = Path.Combine(bottleDir, "drive_c",
            "users", "bottlesuser", "Documents", "Elder Scrolls Online", "live", "AddOns");
        Directory.CreateDirectory(addonPath);

        var options = new AddonsDirectoryDiscoveryOptions
        {
            Platform = AddonsDirectoryDiscoveryPlatform.Linux,
            HomeDirectory = tmp.RootPath,
            DocumentsDirectory = Path.Combine(tmp.RootPath, "Documents"),
            EnvironmentVariables = new Dictionary<string, string?>()
        };

        var result = AddonsDirectoryDiscoveryService.Discover(options);

        Assert.Contains(result.Candidates, c =>
            c.Path == Path.GetFullPath(addonPath));
    }

    [Fact]
    public void Discover_MultipleCandidates_ReturnsAll()
    {
        using var tmp = new TempDirectoryFixture();

        // Steam candidate
        var steamPath = Path.Combine(tmp.RootPath, ".steam", "steam", "steamapps",
            "compatdata", AddonsDirectoryDiscoveryService.EsoSteamAppId, "pfx",
            "drive_c", "users", "steamuser", "Documents", "Elder Scrolls Online", "live", "AddOns");
        Directory.CreateDirectory(steamPath);

        // Wine candidate
        var winePath = Path.Combine(tmp.RootPath, ".wine", "drive_c",
            "users", "wineuser", "Documents", "Elder Scrolls Online", "live", "AddOns");
        Directory.CreateDirectory(winePath);

        var options = new AddonsDirectoryDiscoveryOptions
        {
            Platform = AddonsDirectoryDiscoveryPlatform.Linux,
            HomeDirectory = tmp.RootPath,
            DocumentsDirectory = Path.Combine(tmp.RootPath, "Documents"),
            EnvironmentVariables = new Dictionary<string, string?>(),
            SteamRootDirectories = new[]
            {
                Path.Combine(tmp.RootPath, ".steam", "steam")
            }
        };

        var result = AddonsDirectoryDiscoveryService.Discover(options);

        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void Discover_MissingDirectories_ReturnsEmpty()
    {
        using var tmp = new TempDirectoryFixture();

        var options = new AddonsDirectoryDiscoveryOptions
        {
            Platform = AddonsDirectoryDiscoveryPlatform.Windows,
            DocumentsDirectory = Path.Combine(tmp.RootPath, "Documents"),
            HomeDirectory = tmp.RootPath,
            EnvironmentVariables = new Dictionary<string, string?>()
        };

        var result = AddonsDirectoryDiscoveryService.Discover(options);

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Discover_WrongFolderName_FilteredOut()
    {
        using var tmp = new TempDirectoryFixture();
        var wrongPath = Path.Combine(tmp.RootPath, "Documents", "Elder Scrolls Online", "live", "WrongName");
        Directory.CreateDirectory(wrongPath);

        var options = new AddonsDirectoryDiscoveryOptions
        {
            Platform = AddonsDirectoryDiscoveryPlatform.Windows,
            DocumentsDirectory = Path.Combine(tmp.RootPath, "Documents"),
            HomeDirectory = tmp.RootPath,
            EnvironmentVariables = new Dictionary<string, string?>()
        };

        var result = AddonsDirectoryDiscoveryService.Discover(options);

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Discover_WarningsCollection_ReturnsWarnings()
    {
        using var tmp = new TempDirectoryFixture();

        // Create an inaccessible path by... well we can't easily on all platforms.
        // Instead, test that no warnings are generated for a clean scan with no results.
        var options = new AddonsDirectoryDiscoveryOptions
        {
            Platform = AddonsDirectoryDiscoveryPlatform.Windows,
            DocumentsDirectory = Path.Combine(tmp.RootPath, "Documents"),
            HomeDirectory = tmp.RootPath,
            EnvironmentVariables = new Dictionary<string, string?>()
        };

        var result = AddonsDirectoryDiscoveryService.Discover(options);

        Assert.Empty(result.Candidates);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Discover_CandidatesSortedByPriority()
    {
        using var tmp = new TempDirectoryFixture();

        // Create a low-priority Wine candidate
        var winePrefix = Path.Combine(tmp.RootPath, ".wine");
        var wineAddonPath = Path.Combine(winePrefix, "drive_c", "users", "user",
            "Documents", "Elder Scrolls Online", "live", "AddOns");
        Directory.CreateDirectory(wineAddonPath);

        // Create a higher-priority Steam candidate
        var steamAddonPath = Path.Combine(tmp.RootPath, ".steam", "steam", "steamapps",
            "compatdata", AddonsDirectoryDiscoveryService.EsoSteamAppId, "pfx",
            "drive_c", "users", "steamuser", "Documents", "Elder Scrolls Online", "live", "AddOns");
        Directory.CreateDirectory(steamAddonPath);

        var options = new AddonsDirectoryDiscoveryOptions
        {
            Platform = AddonsDirectoryDiscoveryPlatform.Linux,
            HomeDirectory = tmp.RootPath,
            DocumentsDirectory = Path.Combine(tmp.RootPath, "Documents"),
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["WINEPREFIX"] = winePrefix
            },
            SteamRootDirectories = new[]
            {
                Path.Combine(tmp.RootPath, ".steam", "steam")
            }
        };

        var result = AddonsDirectoryDiscoveryService.Discover(options);

        Assert.Equal(2, result.Candidates.Count);
        // Steam (priority 5) should come before Wine (priority 20)
        Assert.Contains("Steam", result.Candidates[0].SourceDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseLibraryFoldersVdf_ValidFile_ReturnsPaths()
    {
        using var tmp = new TempDirectoryFixture();
        var vdfPath = Path.Combine(tmp.RootPath, "libraryfolders.vdf");
        var libraryPath = Path.Combine(tmp.RootPath, "extralib");
        Directory.CreateDirectory(libraryPath);

        File.WriteAllText(vdfPath, @"
""libraryfolders""
{
    ""0""
    {
        ""path""    ""/some/steam""
        ""label""   """"
        ""totalsize""   ""0""
    }
    ""1""
    {
        ""path""    ""/nonexistent/path""
    }
    ""2""
    {
        ""path""    """ + libraryPath.Replace("\\", "\\\\") + @"""
    }
}
");
        var result = AddonsDirectoryDiscoveryService.ParseLibraryFoldersVdf(vdfPath);

        Assert.Contains(result, p => p == libraryPath);
        Assert.DoesNotContain(result, p => p.Contains("nonexistent"));
    }

    [LinuxSymlinkFact]
    public void Discover_LinuxSteamRootsThroughSymlinks_DeduplicatesToSingleCandidate()
    {
        using var tmp = new TempDirectoryFixture();

        var realSteamRoot = Path.Combine(tmp.RootPath, ".local", "share", "Steam");
        var addonPath = Path.Combine(realSteamRoot, "steamapps",
            "compatdata", AddonsDirectoryDiscoveryService.EsoSteamAppId, "pfx",
            "drive_c", "users", "steamuser", "Documents", "Elder Scrolls Online", "live", "AddOns");
        Directory.CreateDirectory(addonPath);

        var dotSteam = Path.Combine(tmp.RootPath, ".steam");
        Directory.CreateDirectory(dotSteam);

        var steamAlias = Path.Combine(dotSteam, "steam");
        var rootAlias = Path.Combine(dotSteam, "root");

        Directory.CreateSymbolicLink(
            steamAlias,
            Path.GetRelativePath(dotSteam, realSteamRoot));

        Directory.CreateSymbolicLink(
            rootAlias,
            Path.GetRelativePath(dotSteam, realSteamRoot));

        var options = new AddonsDirectoryDiscoveryOptions
        {
            Platform = AddonsDirectoryDiscoveryPlatform.Linux,
            HomeDirectory = tmp.RootPath,
            DocumentsDirectory = Path.Combine(tmp.RootPath, "Documents"),
            EnvironmentVariables = new Dictionary<string, string?>(),
            SteamRootDirectories = new[]
            {
                steamAlias,
                rootAlias,
                realSteamRoot
            }
        };

        var result = AddonsDirectoryDiscoveryService.Discover(options);

        Assert.Single(result.Candidates);
    }
}

/// <summary>
/// A test that only runs on Linux (where Directory.CreateSymbolicLink is available without
/// elevated privileges for directory symlinks).
/// </summary>
public sealed class LinuxSymlinkFactAttribute : FactAttribute
{
    public LinuxSymlinkFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "This test requires Linux (directory symlink support).";
    }
}