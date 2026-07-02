using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

public sealed class AddonInstallationService : IAddonInstallationService
{
    private readonly ISingleAddonInstallationWorker _worker;
    private readonly IAddonDataManager _addonDataManager;

    public AddonInstallationService(
        IAddonOperationJournalStore journalStore,
        IAddonDataManager addonDataManager,
        IArchiveDownloader archiveDownloader,
        IEsoDataConnectionFactory dbConnectionFactory)
    {
        ArgumentNullException.ThrowIfNull(journalStore);
        ArgumentNullException.ThrowIfNull(addonDataManager);
        ArgumentNullException.ThrowIfNull(archiveDownloader);
        ArgumentNullException.ThrowIfNull(dbConnectionFactory);

        _addonDataManager = addonDataManager;
        _worker = new SingleAddonInstallationWorker(
            journalStore, addonDataManager, archiveDownloader, dbConnectionFactory);
    }

    public async Task<InstallResult> InstallAsync(
        Addon addon,
        AddonInstallationMethod installationMethod,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _worker.InstallOneAsync(addon, installationMethod, progress, cancellationToken);

            if (result.Succeeded &&
                recursive &&
                !result.CompletedAfterCancellation &&
                !cancellationToken.IsCancellationRequested)
                await InstallDependenciesAsync(addon, installationMethod, progress, cancellationToken);

            return result;
        }
        catch (OperationCanceledException ex)
        {
            return InstallResult.Canceled(
                $"Installation of addon {addon.Name} was canceled before it started.",
                ex);
        }
    }

    public async Task<InstallResult> UpdateAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        Debug.WriteLine($"Updating addon {addon.Name}");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _worker.ReplaceOneAsync(
                addon, AddonOperationType.Update, addon.InstallationMethod, progress, cancellationToken);

            if (result.Succeeded &&
                recursive &&
                !result.CompletedAfterCancellation &&
                !cancellationToken.IsCancellationRequested)
                await InstallDependenciesAsync(addon, addon.InstallationMethod, progress, cancellationToken);

            return result;
        }
        catch (OperationCanceledException ex)
        {
            return InstallResult.Canceled(
                $"Update of addon {addon.Name} was canceled before it started.",
                ex);
        }
    }

    public async Task<InstallResult> ReinstallAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _worker.ReplaceOneAsync(
                addon, AddonOperationType.Reinstall, addon.InstallationMethod, progress, cancellationToken);

            if (result.Succeeded &&
                recursive &&
                !result.CompletedAfterCancellation &&
                !cancellationToken.IsCancellationRequested)
                await InstallDependenciesAsync(addon, addon.InstallationMethod, progress, cancellationToken);

            return result;
        }
        catch (OperationCanceledException ex)
        {
            return InstallResult.Canceled(
                $"Reinstall of addon {addon.Name} was canceled before it started.",
                ex);
        }
    }

    public async Task<InstallResult> DeleteAsync(
        Addon addon,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _worker.DeleteOne(addon, progress, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            return InstallResult.Canceled(
                $"Delete of addon {addon.Name} was canceled before it started.",
                ex);
        }
    }

    // ---- Dependencies ----

    private async Task InstallDependenciesAsync(
        Addon addon,
        AddonInstallationMethod installationMethod,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        Debug.WriteLine($"Installing {addon.Name} dependencies: {string.Join(", ", addon.LocalDependencies.Select(d => d.Name))}");

        foreach (var dependency in addon.LocalDependencies)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var depAddon = _addonDataManager.OnlineAddons.FirstOrDefault(a => a.CommonAddonId == dependency.Id);

            if (depAddon == null)
            {
                Debug.WriteLine($"Dependency {dependency.Name} not found!");
                continue;
            }

            InstallResult result;

            switch (depAddon.State)
            {
                case AddonState.LatestVersion:
                case AddonState.Installing:
                    continue;

                case AddonState.NotInstalled:
                    result = await InstallAsync(depAddon, AddonInstallationMethod.Dependency,
                        true, progress, cancellationToken);
                    break;

                case AddonState.Outdated:
                    result = await UpdateAsync(depAddon, true, progress, cancellationToken);
                    break;

                case AddonState.InstallationError:
                    result = await ReinstallAsync(depAddon, true, progress, cancellationToken);
                    break;

                default:
                    continue;
            }

            if (!result.Succeeded && !result.WasCanceled)
                Debug.WriteLine($"Dependency {depAddon.Name} failed: {result.ErrorMessage}");
        }
    }
}