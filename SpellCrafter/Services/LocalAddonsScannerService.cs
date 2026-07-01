using SpellCrafter.Enums;
using SpellCrafter.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System;

namespace SpellCrafter.Services;

public static class LocalAddonsScannerService
{
    public static event EventHandler? ScanningChanged;

    private static bool _isScanning;

    public static bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (_isScanning != value)
            {
                _isScanning = value;
                ScanningChanged?.Invoke(null, EventArgs.Empty);
            }
        }
    }

    public static List<Addon>? ScanDirectory(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (IsScanning) return null;
        IsScanning = true;

        var addons = new List<Addon>();

        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            IsScanning = false;
            return addons;
        }

        foreach (var addonDir in Directory.GetDirectories(path))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var addonName = Path.GetFileName(addonDir);
            var addonManifest = Path.Combine(addonDir, $"{addonName}.txt");

            if (!File.Exists(addonManifest))
                continue;

            var addon = AddonManifestParser.ParseAddonManifest(addonManifest, false);
            addon.State = AddonState.LatestVersion; // TODO check latest version

            addons.Add(addon);
        }

        IsScanning = false;
        return addons;
    }
}