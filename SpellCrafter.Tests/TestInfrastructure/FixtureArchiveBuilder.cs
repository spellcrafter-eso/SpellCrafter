using System.IO.Compression;
using System.Text;

namespace SpellCrafter.Tests.TestInfrastructure;

/// <summary>
/// Helper to create fixture ZIP archives in code for testing.
/// </summary>
internal static class FixtureArchiveBuilder
{
    /// <summary>
    /// Creates a ZIP archive at <paramref name="archivePath"/> with entries that simulate
    /// an addon named <paramref name="addonName"/>.
    /// </summary>
    public static string CreateValidAddonArchive(string addonName, string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var entry = archive.CreateEntry($"{addonName}/{addonName}.txt");
        using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
        {
            writer.WriteLine($"## {addonName}");
            writer.WriteLine("## Version: 1.0");
        }

        entry = archive.CreateEntry($"{addonName}/file.lua");
        using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- test lua file");
        }

        return archivePath;
    }

    /// <summary>
    /// Creates a ZIP archive with an entry that attempts path traversal.
    /// </summary>
    public static string CreateTraversalArchive(string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("../../etc/passwd");
        using var writer = new StreamWriter(entry.Open());
        writer.WriteLine("root:x:0:0:root:");
        return archivePath;
    }

    /// <summary>
    /// Creates a ZIP archive with a rooted absolute path entry.
    /// </summary>
    public static string CreateRootedArchive(string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("/etc/passwd");
        using var writer = new StreamWriter(entry.Open());
        writer.WriteLine("root:x:0:0:root:");
        return archivePath;
    }

    /// <summary>
    /// Creates an empty ZIP archive.
    /// </summary>
    public static string CreateEmptyArchive(string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        // No entries
        return archivePath;
    }

    /// <summary>
    /// Creates a ZIP archive with two entries that would map to the same destination path.
    /// </summary>
    public static string CreateDuplicateArchive(string addonName, string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var entry1 = archive.CreateEntry($"{addonName}/same.txt");
        using (var writer = new StreamWriter(entry1.Open()))
        {
            writer.WriteLine("content1");
        }

        var entry2 = archive.CreateEntry($"{addonName}/same.txt");
        using (var writer = new StreamWriter(entry2.Open()))
        {
            writer.WriteLine("content2");
        }

        return archivePath;
    }

    /// <summary>
    /// Creates a ZIP archive with entries that belong to a different addon name.
    /// </summary>
    public static string CreateWrongAddonArchive(string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var entry = archive.CreateEntry("OtherAddon/OtherAddon.txt");
        using var writer = new StreamWriter(entry.Open());
        writer.WriteLine("wrong addon");

        return archivePath;
    }

    /// <summary>
    /// Creates a ZIP archive with a valid addon folder entry AND a path traversal entry
    /// inside the addon folder (e.g., "TestAddon/../evil.lua").
    /// </summary>
    public static string CreateArchiveWithAddonTraversalEntry(string addonName, string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var manifest = archive.CreateEntry($"{addonName}/{addonName}.txt");
        using (var writer = new StreamWriter(manifest.Open(), Encoding.UTF8))
        {
            writer.WriteLine($"## {addonName}");
            writer.WriteLine("## Version: 1.0");
        }

        var safe = archive.CreateEntry($"{addonName}/file.lua");
        using (var writer = new StreamWriter(safe.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- safe file");
        }

        var traversal = archive.CreateEntry($"{addonName}/../evil.lua");
        using (var writer = new StreamWriter(traversal.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- traversal file");
        }

        return archivePath;
    }

    /// <summary>
    /// Creates a ZIP archive with a rooted Unix absolute path entry alongside valid addon entries.
    /// </summary>
    public static string CreateArchiveWithRootedUnixEntry(string addonName, string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var manifest = archive.CreateEntry($"{addonName}/{addonName}.txt");
        using (var writer = new StreamWriter(manifest.Open(), Encoding.UTF8))
        {
            writer.WriteLine($"## {addonName}");
            writer.WriteLine("## Version: 1.0");
        }

        var safe = archive.CreateEntry($"{addonName}/file.lua");
        using (var writer = new StreamWriter(safe.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- safe file");
        }

        var rooted = archive.CreateEntry("/tmp/evil.lua");
        using (var writer = new StreamWriter(rooted.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- rooted file");
        }

        return archivePath;
    }

    /// <summary>
    /// Creates a ZIP archive with a Windows-rooted path entry (e.g., "C:\evil.lua") alongside valid addon entries.
    /// </summary>
    public static string CreateArchiveWithWindowsRootedEntry(string addonName, string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var manifest = archive.CreateEntry($"{addonName}/{addonName}.txt");
        using (var writer = new StreamWriter(manifest.Open(), Encoding.UTF8))
        {
            writer.WriteLine($"## {addonName}");
            writer.WriteLine("## Version: 1.0");
        }

        var safe = archive.CreateEntry($"{addonName}/file.lua");
        using (var writer = new StreamWriter(safe.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- safe file");
        }

        var rooted = archive.CreateEntry("C:\\evil.lua");
        using (var writer = new StreamWriter(rooted.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- windows-rooted file");
        }

        return archivePath;
    }

    /// <summary>
    /// Creates a ZIP archive with an addon folder but without the required manifest (.txt) file.
    /// </summary>
    public static string CreateArchiveMissingManifest(string addonName, string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var entry = archive.CreateEntry($"{addonName}/file.lua");
        using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- no manifest present");
        }

        return archivePath;
    }

    /// <summary>
    /// Creates a ZIP archive with files at root level (no addon subfolder).
    /// </summary>
    public static string CreateArchiveFilesAtRoot(string addonName, string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var entry = archive.CreateEntry($"{addonName}.txt");
        using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
        {
            writer.WriteLine($"## {addonName}");
            writer.WriteLine("## Version: 1.0");
        }

        entry = archive.CreateEntry("file.lua");
        using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- root level file");
        }

        return archivePath;
    }

    /// <summary>
    /// Creates a ZIP archive with only directory entries (no files).
    /// </summary>
    public static string CreateArchiveOnlyDirectories(string addonName, string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        archive.CreateEntry($"{addonName}/");
        archive.CreateEntry($"{addonName}/SubFolder/");

        return archivePath;
    }

    /// <summary>
    /// Creates a ZIP archive with entries that collide case-insensitively.
    /// </summary>
    public static string CreateArchiveWithCaseDuplicatePaths(string addonName, string archivePath)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var manifest = archive.CreateEntry($"{addonName}/{addonName}.txt");
        using (var writer = new StreamWriter(manifest.Open(), Encoding.UTF8))
        {
            writer.WriteLine($"## {addonName}");
            writer.WriteLine("## Version: 1.0");
        }

        var upper = archive.CreateEntry($"{addonName}/File.lua");
        using (var writer = new StreamWriter(upper.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- upper-case path");
        }

        var lower = archive.CreateEntry($"{addonName}/file.lua");
        using (var writer = new StreamWriter(lower.Open(), Encoding.UTF8))
        {
            writer.WriteLine("-- lower-case path");
        }

        return archivePath;
    }
}