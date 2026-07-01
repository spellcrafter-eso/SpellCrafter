using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SpellCrafter.Services;

public static class AddonsDirectoryDiscoveryService
{
    public const string EsoSteamAppId = "306130";
    private const string AddonsFolderName = "AddOns";
    private const int MaxSymlinkResolutionDepth = 64;

    public static AddonsDirectoryDiscoveryResult Discover()
    {
        var platform = GetCurrentPlatform();
        var options = new AddonsDirectoryDiscoveryOptions
        {
            Platform = platform,
            HomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            DocumentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["STEAM_COMPAT_DATA_PATH"] = Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH"),
                ["WINEPREFIX"] = Environment.GetEnvironmentVariable("WINEPREFIX"),
                ["HOME"] = Environment.GetEnvironmentVariable("HOME")
            }
        };

        return Discover(options);
    }

    internal static AddonsDirectoryDiscoveryResult Discover(AddonsDirectoryDiscoveryOptions options)
    {
        var candidates = new List<AddonsDirectoryCandidate>();
        var warnings = new List<string>();
        var seenPaths = new HashSet<string>(GetPathComparer(options.Platform));

        switch (options.Platform)
        {
            case AddonsDirectoryDiscoveryPlatform.Windows:
                DiscoverWindowsCandidates(candidates, seenPaths, warnings, options);
                break;
            case AddonsDirectoryDiscoveryPlatform.MacOS:
                DiscoverMacOsCandidates(candidates, seenPaths, warnings, options);
                break;
            case AddonsDirectoryDiscoveryPlatform.Linux:
                DiscoverLinuxCandidates(candidates, seenPaths, warnings, options);
                break;
        }

        candidates.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        return new AddonsDirectoryDiscoveryResult(candidates, warnings);
    }

    private static void DiscoverWindowsCandidates(
        List<AddonsDirectoryCandidate> candidates,
        HashSet<string> seenPaths,
        List<string> warnings,
        AddonsDirectoryDiscoveryOptions options)
    {
        var docsDir = options.DocumentsDirectory;
        if (!string.IsNullOrEmpty(docsDir))
        {
            TryAddCandidate(candidates, seenPaths, warnings,
                Path.Combine(docsDir, "Elder Scrolls Online", "live", AddonsFolderName),
                "Windows Documents",
                "Standard Windows Documents folder",
                0,
                options.Platform);

            TryAddCandidate(candidates, seenPaths, warnings,
                Path.Combine(docsDir, "Elder Scrolls Online", "live", "Addons"),
                "Windows Documents (addons lowercase)",
                "Standard Windows Documents folder",
                10,
                options.Platform);
        }
    }

    private static void DiscoverMacOsCandidates(
        List<AddonsDirectoryCandidate> candidates,
        HashSet<string> seenPaths,
        List<string> warnings,
        AddonsDirectoryDiscoveryOptions options)
    {
        var docsDir = options.DocumentsDirectory;
        if (!string.IsNullOrEmpty(docsDir))
            TryAddCandidate(candidates, seenPaths, warnings,
                Path.Combine(docsDir, "Elder Scrolls Online", "live", AddonsFolderName),
                "macOS Documents",
                "Standard macOS Documents folder",
                0,
                options.Platform);

        // Also check ~/Documents as fallback
        var home = options.HomeDirectory;
        if (!string.IsNullOrEmpty(home) && string.IsNullOrEmpty(docsDir))
            TryAddCandidate(candidates, seenPaths, warnings,
                Path.Combine(home, "Documents", "Elder Scrolls Online", "live", AddonsFolderName),
                "macOS Documents (HOME/Documents)",
                "macOS Documents via HOME fallback",
                10,
                options.Platform);
    }

    private static void DiscoverLinuxCandidates(
        List<AddonsDirectoryCandidate> candidates,
        HashSet<string> seenPaths,
        List<string> warnings,
        AddonsDirectoryDiscoveryOptions options)
    {
        // 1. STEAM_COMPAT_DATA_PATH
        var compatDataPath = options.EnvironmentVariables?.GetValueOrDefault("STEAM_COMPAT_DATA_PATH");
        if (!string.IsNullOrEmpty(compatDataPath))
            DiscoverProtonPrefix(candidates, seenPaths, warnings, compatDataPath,
                "STEAM_COMPAT_DATA_PATH", 0, options);

        // 2. Known Steam roots
        var steamRoots = options.SteamRootDirectories ?? GetDefaultSteamRoots(options);
        foreach (var steamRoot in steamRoots)
        {
            if (!Directory.Exists(steamRoot))
                continue;

            // Check compatdata/306130
            var compatDir = Path.Combine(steamRoot, "steamapps", "compatdata", EsoSteamAppId);
            if (Directory.Exists(compatDir))
                DiscoverProtonPrefix(candidates, seenPaths, warnings, compatDir,
                    $"Steam root: {steamRoot}", 5, options);

            // Parse libraryfolders.vdf for external libraries
            var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
                try
                {
                    foreach (var libPath in ParseLibraryFoldersVdf(vdfPath))
                    {
                        var extCompatDir = Path.Combine(libPath, "steamapps", "compatdata", EsoSteamAppId);
                        if (Directory.Exists(extCompatDir) &&
                            !Path.GetFullPath(extCompatDir).StartsWith(Path.GetFullPath(steamRoot), StringComparison.Ordinal))
                            DiscoverProtonPrefix(candidates, seenPaths, warnings, extCompatDir,
                                $"Steam library: {libPath}", 6, options);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Could not read Steam library folders file '{vdfPath}': {ex.Message}");
                }
        }

        // 3. WINEPREFIX
        var winePrefix = options.EnvironmentVariables?.GetValueOrDefault("WINEPREFIX");
        if (!string.IsNullOrEmpty(winePrefix))
            DiscoverWinePrefix(candidates, seenPaths, warnings, winePrefix,
                "WINEPREFIX environment variable", 10, options);

        // 4. Default ~/.wine
        var home = options.HomeDirectory;
        if (!string.IsNullOrEmpty(home))
        {
            var defaultWine = Path.Combine(home, ".wine");
            if (Directory.Exists(defaultWine))
                DiscoverWinePrefix(candidates, seenPaths, warnings, defaultWine,
                    "Default Wine prefix (~/.wine)", 20, options);

            // 5. Bottles
            DiscoverBottlesPrefixes(candidates, seenPaths, warnings, options);

            // 6. ~/Documents fallback
            TryAddCandidate(candidates, seenPaths, warnings,
                Path.Combine(home, "Documents", "Elder Scrolls Online", "live", AddonsFolderName),
                "Linux Documents fallback",
                "Home Documents folder fallback",
                50,
                options.Platform);
        }
    }

    private static void DiscoverProtonPrefix(
        List<AddonsDirectoryCandidate> candidates,
        HashSet<string> seenPaths,
        List<string> warnings,
        string compatDataPath,
        string sourceDescription,
        int priority,
        AddonsDirectoryDiscoveryOptions options)
    {
        var pfxDir = Path.Combine(compatDataPath, "pfx");
        if (!Directory.Exists(pfxDir))
            return;

        var driveC = Path.Combine(pfxDir, "drive_c");
        if (!Directory.Exists(driveC))
            return;

        var usersDir = Path.Combine(driveC, "users");
        if (!Directory.Exists(usersDir))
            return;

        try
        {
            foreach (var userDir in Directory.GetDirectories(usersDir))
            {
                var userName = Path.GetFileName(userDir);
                if (userName.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
                    userName.Equals("All Users", StringComparison.OrdinalIgnoreCase) ||
                    userName.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                    userName.Equals("Default User", StringComparison.OrdinalIgnoreCase))
                    continue;

                var esoDocs = Path.Combine(userDir, "Documents", "Elder Scrolls Online", "live", AddonsFolderName);
                TryAddCandidate(candidates, seenPaths, warnings, esoDocs,
                    $"Proton/Wine user: {userName}",
                    sourceDescription,
                    priority,
                    options.Platform);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not enumerate users in '{usersDir}': {ex.Message}");
        }
    }

    private static void DiscoverWinePrefix(
        List<AddonsDirectoryCandidate> candidates,
        HashSet<string> seenPaths,
        List<string> warnings,
        string winePrefix,
        string sourceDescription,
        int priority,
        AddonsDirectoryDiscoveryOptions options)
    {
        var driveC = Path.Combine(winePrefix, "drive_c");
        if (!Directory.Exists(driveC))
            return;

        var usersDir = Path.Combine(driveC, "users");
        if (!Directory.Exists(usersDir))
            return;

        try
        {
            foreach (var userDir in Directory.GetDirectories(usersDir))
            {
                var userName = Path.GetFileName(userDir);
                if (userName.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
                    userName.Equals("All Users", StringComparison.OrdinalIgnoreCase) ||
                    userName.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                    userName.Equals("Default User", StringComparison.OrdinalIgnoreCase))
                    continue;

                var esoDocs = Path.Combine(userDir, "Documents", "Elder Scrolls Online", "live", AddonsFolderName);
                TryAddCandidate(candidates, seenPaths, warnings, esoDocs,
                    $"Wine user: {userName}",
                    sourceDescription,
                    priority,
                    options.Platform);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not enumerate users in '{usersDir}': {ex.Message}");
        }
    }

    private static void DiscoverBottlesPrefixes(
        List<AddonsDirectoryCandidate> candidates,
        HashSet<string> seenPaths,
        List<string> warnings,
        AddonsDirectoryDiscoveryOptions options)
    {
        var home = options.HomeDirectory;
        if (string.IsNullOrEmpty(home))
            return;

        var bottlesPaths = new[]
        {
            Path.Combine(home, ".local", "share", "bottles", "bottles"),
            Path.Combine(home, ".var", "app", "com.usebottles.bottles", "data", "bottles", "bottles")
        };

        foreach (var bottlesDir in bottlesPaths)
        {
            if (!Directory.Exists(bottlesDir))
                continue;

            try
            {
                foreach (var bottleDir in Directory.GetDirectories(bottlesDir))
                {
                    var bottleDriveC = Path.Combine(bottleDir, "drive_c");
                    if (!Directory.Exists(bottleDriveC))
                        continue;

                    var usersDir = Path.Combine(bottleDriveC, "users");
                    if (!Directory.Exists(usersDir))
                        continue;

                    foreach (var userDir in Directory.GetDirectories(usersDir))
                    {
                        var userName = Path.GetFileName(userDir);
                        if (userName is "Public" or "All Users" or "Default" or "Default User")
                            continue;

                        var esoDocs = Path.Combine(userDir, "Documents", "Elder Scrolls Online", "live", AddonsFolderName);
                        TryAddCandidate(candidates, seenPaths, warnings, esoDocs,
                            $"Bottle: {Path.GetFileName(bottleDir)}, user: {userName}",
                            $"Bottles prefix: {bottleDir}",
                            30,
                            options.Platform);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not enumerate Bottles in '{bottlesDir}': {ex.Message}");
            }
        }
    }

    private static List<string> GetDefaultSteamRoots(AddonsDirectoryDiscoveryOptions options)
    {
        var roots = new List<string>();
        var home = options.HomeDirectory;
        if (string.IsNullOrEmpty(home))
            return roots;

        var candidates = new[]
        {
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".steam", "root"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam")
        };

        foreach (var candidate in candidates)
        {
            var normalized = Path.GetFullPath(candidate);
            if (Directory.Exists(normalized) && !roots.Contains(normalized, StringComparer.Ordinal))
                roots.Add(normalized);
        }

        return roots;
    }

    internal static List<string> ParseLibraryFoldersVdf(string vdfPath)
    {
        var libraries = new List<string>();
        var lines = File.ReadAllLines(vdfPath);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('"') && trimmed.Contains("\"path\""))
            {
                var parts = trimmed.Split('"');
                for (var i = 0; i < parts.Length - 1; i++)
                    if (parts[i] == "path" && i + 2 < parts.Length)
                    {
                        var path = parts[i + 2];
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            // Normalize: remove quotes, handle escapes
                            path = path.Replace("\\\\", "\\");
                            if (Directory.Exists(path))
                                libraries.Add(path);
                        }

                        break;
                    }
            }
        }

        return libraries;
    }

    private static void TryAddCandidate(
        List<AddonsDirectoryCandidate> candidates,
        HashSet<string> seenPaths,
        List<string> warnings,
        string path,
        string displayName,
        string sourceDescription,
        int priority,
        AddonsDirectoryDiscoveryPlatform platform)
    {
        string normalizedPath;
        try
        {
            normalizedPath = NormalizeDirectoryPath(path);
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            warnings.Add($"Invalid path '{path}': {ex.Message}");
            return;
        }

        if (!Directory.Exists(normalizedPath))
            return;

        // Validate the folder name is "AddOns" (case-insensitive)
        if (!AddonsDirectoryValidator.IsValidAddonsDirectory(normalizedPath))
            return;

        // Resolve symlinks for deduplication key, then deduplicate
        var deduplicationPath = GetDeduplicationPath(normalizedPath, platform, warnings);
        if (!seenPaths.Add(deduplicationPath))
            return;

        candidates.Add(new AddonsDirectoryCandidate(
            normalizedPath,
            displayName,
            sourceDescription,
            priority));
    }

    /// <summary>
    /// Normalizes a directory path: trims trailing separators and resolves full path.
    /// </summary>
    private static string NormalizeDirectoryPath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    /// <summary>
    /// Builds a deduplication key by resolving symbolic links in the path.
    /// If resolution fails, falls back to the normalized path.
    /// </summary>
    private static string GetDeduplicationPath(
        string normalizedPath,
        AddonsDirectoryDiscoveryPlatform platform,
        List<string> warnings)
    {
        try
        {
            return ResolveExistingDirectoryPath(normalizedPath, platform);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            warnings.Add(
                $"Could not resolve symbolic links for '{normalizedPath}'; " +
                $"duplicate detection will use the unresolved path. {ex.Message}");
            return normalizedPath;
        }
    }

    /// <summary>
    /// Resolves all symbolic links in an existing directory path component by component,
    /// producing a canonical real path. Detects circular symlinks and enforces a depth limit.
    /// </summary>
    private static string ResolveExistingDirectoryPath(
        string normalizedPath,
        AddonsDirectoryDiscoveryPlatform platform)
    {
        var comparer = GetPathComparer(platform);
        var seenResolutionPaths = new HashSet<string>(comparer);
        var currentPath = normalizedPath;

        for (var depth = 0; depth < MaxSymlinkResolutionDepth; depth++)
        {
            currentPath = NormalizeDirectoryPath(currentPath);

            if (!seenResolutionPaths.Add(currentPath))
                throw new IOException(
                    $"Circular symbolic link encountered while resolving '{normalizedPath}'.");

            var nextPath = ResolveFirstDirectoryLinkInPath(currentPath);
            if (nextPath == null)
                return currentPath;

            currentPath = nextPath;
        }

        throw new IOException(
            $"Too many symbolic links while resolving '{normalizedPath}' (max {MaxSymlinkResolutionDepth}).");
    }

    /// <summary>
    /// Walks a directory path component by component and returns a new path with the first
    /// symlink component resolved to its target. Returns null if no component is a symlink.
    /// </summary>
    private static string? ResolveFirstDirectoryLinkInPath(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
            return null;

        var relativePath = path[root.Length..];
        if (relativePath.Length == 0)
            return null;

        var separators = Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
            ? new[] { Path.DirectorySeparatorChar }
            : new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        var parts = relativePath.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        var currentPath = root;
        for (var index = 0; index < parts.Length; index++)
        {
            currentPath = Path.Combine(currentPath, parts[index]);

            var currentDirectory = new DirectoryInfo(currentPath);
            if (currentDirectory.LinkTarget == null)
                continue;

            var targetInfo = currentDirectory.ResolveLinkTarget(false);
            if (targetInfo == null)
                continue;

            var targetPath = NormalizeDirectoryPath(targetInfo.FullName);

            // Append remaining path components
            for (var nextIndex = index + 1; nextIndex < parts.Length; nextIndex++)
                targetPath = Path.Combine(targetPath, parts[nextIndex]);

            return targetPath;
        }

        return null;
    }

    private static AddonsDirectoryDiscoveryPlatform GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return AddonsDirectoryDiscoveryPlatform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return AddonsDirectoryDiscoveryPlatform.MacOS;
        return AddonsDirectoryDiscoveryPlatform.Linux;
    }

    private static StringComparer GetPathComparer(AddonsDirectoryDiscoveryPlatform platform)
    {
        return platform == AddonsDirectoryDiscoveryPlatform.Linux
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
    }
}

public sealed class AddonsDirectoryDiscoveryResult
{
    public IReadOnlyList<AddonsDirectoryCandidate> Candidates { get; }
    public IReadOnlyList<string> Warnings { get; }

    public bool HasCandidates => Candidates.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;

    public AddonsDirectoryDiscoveryResult(
        IReadOnlyList<AddonsDirectoryCandidate> candidates,
        IReadOnlyList<string> warnings)
    {
        Candidates = candidates;
        Warnings = warnings;
    }
}

public sealed class AddonsDirectoryCandidate
{
    public string Path { get; }
    public string DisplayName { get; }
    public string SourceDescription { get; }
    public string DisplayText => $"{DisplayName} — {Path}";

    internal int Priority { get; }

    public AddonsDirectoryCandidate(
        string path,
        string displayName,
        string sourceDescription,
        int priority)
    {
        Path = path;
        DisplayName = displayName;
        SourceDescription = sourceDescription;
        Priority = priority;
    }
}

internal enum AddonsDirectoryDiscoveryPlatform
{
    Windows,
    Linux,
    MacOS,
    Other
}

internal sealed class AddonsDirectoryDiscoveryOptions
{
    public AddonsDirectoryDiscoveryPlatform Platform { get; init; } = AddonsDirectoryDiscoveryPlatform.Other;
    public string? HomeDirectory { get; init; }
    public string? DocumentsDirectory { get; init; }
    public IReadOnlyDictionary<string, string?>? EnvironmentVariables { get; init; }
    public IReadOnlyList<string>? SteamRootDirectories { get; init; }
    public IReadOnlyList<string>? WinePrefixDirectories { get; init; }

    // For test use only: allow injecting a different root for Documents lookup
    internal string? TestDocumentsRoot { get; init; }
}