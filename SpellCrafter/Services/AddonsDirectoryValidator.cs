using System;
using System.IO;

namespace SpellCrafter.Services;

public static class AddonsDirectoryValidator
{
    private const string AddonsDirectoryName = "AddOns";

    public static bool IsValidAddonsDirectory(string? directoryPath)
    {
        var directoryName = Path.GetFileName(directoryPath);

        return !string.IsNullOrEmpty(directoryName) &&
               directoryName.Equals(AddonsDirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetValidationError(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return "AddOns directory path is empty.";

        if (!Directory.Exists(directoryPath))
            return $"Directory does not exist: {directoryPath}";

        var directoryName = Path.GetFileName(directoryPath);

        if (string.IsNullOrEmpty(directoryName))
            return $"Path has no directory name: {directoryPath}";

        if (!directoryName.Equals(AddonsDirectoryName, StringComparison.OrdinalIgnoreCase))
            return $"Directory must be named '{AddonsDirectoryName}', got '{directoryName}'.";

        return null;
    }
}