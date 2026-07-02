using System;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

/// <summary>
/// Performs a single addon filesystem operation (install/update/reinstall/delete)
/// without recursive dependency handling. All dependency orchestration is the
/// caller's responsibility.
/// </summary>
public interface ISingleAddonInstallationWorker
{
    /// <summary>
    /// Installs one addon from its online archive into the AddOns directory.
    /// </summary>
    Task<InstallResult> InstallOneAsync(
        Addon addon,
        AddonInstallationMethod installationMethod,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an already-installed addon (update or reinstall).
    /// Backs up the existing version before moving the new one.
    /// </summary>
    Task<InstallResult> ReplaceOneAsync(
        Addon addon,
        string operationName,
        AddonInstallationMethod installationMethod,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one addon from the AddOns directory.
    /// Backs up the addon before deleting it.
    /// </summary>
    InstallResult DeleteOne(
        Addon addon,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);
}