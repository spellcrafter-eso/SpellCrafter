using System;
using System.IO;
using System.Linq;

namespace SpellCrafter.Services;

public static class AddonsDirectoryValidator
{
    private const string AddonsDirectoryName = "AddOns";

    /// <summary>
    /// Lightweight check: verifies only that the directory name is "AddOns" (case-insensitive).
    /// Does not check existence or readability.
    /// </summary>
    public static bool IsValidAddonsDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return false;

        try
        {
            var directoryName = Path.GetFileName(directoryPath);
            return !string.IsNullOrEmpty(directoryName) &&
                   directoryName.Equals(AddonsDirectoryName, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Full validation: checks existence, name, and read access.
    /// Returns null if the directory is valid and usable; an error message otherwise.
    /// </summary>
    public static string? GetValidationError(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return "AddOns directory path is empty.";

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(directoryPath);
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"Invalid path format: {ex.Message}";
        }

        if (!Directory.Exists(fullPath))
            return $"Directory does not exist: {fullPath}";

        var directoryName = Path.GetFileName(fullPath);

        if (string.IsNullOrEmpty(directoryName))
            return $"Path has no directory name: {fullPath}";

        if (!directoryName.Equals(AddonsDirectoryName, StringComparison.OrdinalIgnoreCase))
            return $"Directory must be named '{AddonsDirectoryName}', got '{directoryName}'.";

        // Probe read access: enumerate one entry to verify the directory is readable
        // (Any() on an empty directory returns false without throwing;
        //  an unreadable directory throws UnauthorizedAccessException)
        try
        {
            Directory.EnumerateFileSystemEntries(fullPath).Any();
        }
        catch (UnauthorizedAccessException)
        {
            return $"SpellCrafter does not have permission to read: {fullPath}";
        }
        catch (IOException ex)
        {
            return $"Could not access the AddOns directory: {ex.Message}";
        }

        return null;
    }
}