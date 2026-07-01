using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

public sealed class AddonInstallationService : IAddonInstallationService
{
    private readonly IAddonOperationJournalStore _journalStore;
    private readonly IAddonDataManager _addonDataManager;
    private readonly IArchiveDownloader _archiveDownloader;
    private readonly IEsoDataConnectionFactory _dbConnectionFactory;

    public AddonInstallationService(
        IAddonOperationJournalStore journalStore,
        IAddonDataManager addonDataManager,
        IArchiveDownloader archiveDownloader,
        IEsoDataConnectionFactory dbConnectionFactory)
    {
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
        _addonDataManager = addonDataManager ?? throw new ArgumentNullException(nameof(addonDataManager));
        _archiveDownloader = archiveDownloader ?? throw new ArgumentNullException(nameof(archiveDownloader));
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
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

            var result = await InstallCoreAsync(addon, installationMethod, progress, cancellationToken);

            if (result.Succeeded &&
                recursive &&
                !result.CompletedAfterCancellation &&
                !cancellationToken.IsCancellationRequested)
            {
                await InstallDependenciesAsync(addon, installationMethod, progress, cancellationToken);
            }

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

            var result = await ReplaceCoreAsync(
                addon, AddonOperationType.Update, addon.InstallationMethod, progress, cancellationToken);

            if (result.Succeeded &&
                recursive &&
                !result.CompletedAfterCancellation &&
                !cancellationToken.IsCancellationRequested)
            {
                await InstallDependenciesAsync(addon, addon.InstallationMethod, progress, cancellationToken);
            }

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

            var result = await ReplaceCoreAsync(
                addon, AddonOperationType.Reinstall, addon.InstallationMethod, progress, cancellationToken);

            if (result.Succeeded &&
                recursive &&
                !result.CompletedAfterCancellation &&
                !cancellationToken.IsCancellationRequested)
            {
                await InstallDependenciesAsync(addon, addon.InstallationMethod, progress, cancellationToken);
            }

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

            return DeleteCore(addon, progress, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            return InstallResult.Canceled(
                $"Delete of addon {addon.Name} was canceled before it started.",
                ex);
        }
    }

    // ---- Install core ----

    private async Task<InstallResult> InstallCoreAsync(
        Addon addon,
        AddonInstallationMethod installationMethod,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        Debug.WriteLine($"Installing addon {addon.Name}");

        if (addon.UniqueId == null)
            return InstallResult.Failure($"Addon {addon.Name} has no unique online id.");

        var hadLocalAddon = addon.LocalAddonId != null || addon.State != AddonState.NotInstalled;
        var oldState = addon.State;
        var oldInstallationMethod = addon.InstallationMethod;
        var oldVersion = addon.Version;
        var oldDisplayedVersion = addon.DisplayedVersion;

        Addon.AddonOperationPaths? paths = null;
        AddonOperationJournal? journal = null;
        var commitStarted = false;
        var preserveOperationRoot = false;

        try
        {
            paths = CreateOperationPaths(addon, AddonOperationType.Install);

            if (Directory.Exists(paths.TargetAddonDirectory))
            {
                TryDeleteDirectoryIfExists(paths.RootDirectory, out _);
                return InstallResult.Failure($"Target addon folder already exists: {paths.TargetAddonDirectory}");
            }

            journal = _journalStore.Begin(
                addon, AddonOperationType.Install, paths,
                oldState, oldInstallationMethod, oldVersion, oldDisplayedVersion,
                hadLocalAddon, installationMethod);

            _journalStore.SetPhase(journal, AddonOperationPhase.MarkingLocalAddonInstalling);
            ReportProgress(progress, addon.Name, AddonOperationType.Install, InstallProgressStage.Preparing, "Preparing installation...");
            addon.State = AddonState.Installing;
            _addonDataManager.InsertOrUpdateLocalAddon(addon);

            cancellationToken.ThrowIfCancellationRequested();

            _journalStore.SetPhase(journal, AddonOperationPhase.ExtractingToStaging);
            await DownloadAndExtractToStagingAsync(addon, paths, progress, cancellationToken);

            _journalStore.SetPhase(journal, AddonOperationPhase.StagingReady);
            cancellationToken.ThrowIfCancellationRequested();

            ReportProgress(progress, addon.Name, AddonOperationType.Install, InstallProgressStage.Moving, "Moving files to target...");
            _journalStore.SetPhase(journal, AddonOperationPhase.BeforeStagedTargetMove);
            cancellationToken.ThrowIfCancellationRequested();

            commitStarted = true;
            Directory.Move(paths.StagedAddonDirectory, paths.TargetAddonDirectory);
            _journalStore.SetPhase(journal, AddonOperationPhase.AfterStagedTargetMove);

            addon.MarkInstalled(installationMethod);

            ReportProgress(progress, addon.Name, AddonOperationType.Install, InstallProgressStage.Committing, "Committing installation...");
            _journalStore.SetPhase(journal, AddonOperationPhase.BeforeDatabaseCommit);
            _addonDataManager.InsertOrUpdateLocalAddon(addon);

            _journalStore.Complete(journal);

            Debug.WriteLine($"Addon {addon.Name} installed!");

            if (cancellationToken.IsCancellationRequested)
            {
                ReportProgress(progress, addon.Name, AddonOperationType.Install, InstallProgressStage.Completed,
                    "Installation completed after cancellation was requested.");
                return InstallResult.Success(
                    $"Installation of '{addon.Name}' completed after cancellation was requested.",
                    completedAfterCancellation: true);
            }

            ReportProgress(progress, addon.Name, AddonOperationType.Install, InstallProgressStage.Completed,
                $"Addon {addon.Name} installed successfully.");

            return InstallResult.Success();
        }
        catch (OperationCanceledException ex) when (!commitStarted)
        {
            Debug.WriteLine($"Installation of {addon.Name} was canceled before commit.");

            if (!TryRollbackInstall(addon, journal, oldState, oldInstallationMethod,
                    oldVersion, oldDisplayedVersion, hadLocalAddon))
                preserveOperationRoot = true;

            return InstallResult.Canceled(
                $"Installation of addon {addon.Name} was canceled before commit and rolled back.",
                ex);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);

            if (journal != null)
            {
                var errorMessage = $"Failed to install addon {addon.Name}: {ex.Message}";
                _journalStore.SetPhaseWithError(journal, journal.Phase, errorMessage);
            }

            RestoreMetadata(addon, oldState, oldInstallationMethod, oldVersion, oldDisplayedVersion);

            if (!hadLocalAddon)
            {
                _addonDataManager.RemoveLocalAddon(addon);
                addon.LocalAddonId = null;
                addon.State = AddonState.NotInstalled;
            }

            return InstallResult.Failure($"Failed to install addon {addon.Name}.", ex);
        }
        finally
        {
            if (paths != null && !preserveOperationRoot && journal is { IsComplete: true })
                TryDeleteDirectoryIfExists(paths.RootDirectory, out _);

            if (addon.State != AddonState.NotInstalled || hadLocalAddon)
                _addonDataManager.InsertOrUpdateLocalAddon(addon);
        }
    }

    // ---- Replace core (update/reinstall) ----

    private async Task<InstallResult> ReplaceCoreAsync(
        Addon addon,
        string operationName,
        AddonInstallationMethod installationMethod,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        Debug.WriteLine($"{operationName} addon {addon.Name}");

        var oldState = addon.State;
        var oldInstallationMethod = addon.InstallationMethod;
        var oldVersion = addon.Version;
        var oldDisplayedVersion = addon.DisplayedVersion;

        Addon.AddonOperationPaths? paths = null;
        AddonOperationJournal? journal = null;
        var backupMoved = false;
        var preserveOperationRoot = false;
        var commitStarted = false;

        try
        {
            paths = CreateOperationPaths(addon, operationName);

            journal = _journalStore.Begin(
                addon, operationName, paths,
                oldState, oldInstallationMethod, oldVersion, oldDisplayedVersion,
                true,
                installationMethod);

            _journalStore.SetPhase(journal, AddonOperationPhase.MarkingLocalAddonInstalling);
            ReportProgress(progress, addon.Name, operationName, InstallProgressStage.Preparing, $"Preparing {operationName}...");
            addon.State = AddonState.Installing;
            _addonDataManager.InsertOrUpdateLocalAddon(addon);

            cancellationToken.ThrowIfCancellationRequested();

            _journalStore.SetPhase(journal, AddonOperationPhase.ExtractingToStaging);
            await DownloadAndExtractToStagingAsync(addon, paths, progress, cancellationToken);

            _journalStore.SetPhase(journal, AddonOperationPhase.StagingReady);
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(paths.TargetAddonDirectory))
            {
                commitStarted = true;
                _journalStore.SetPhase(journal, AddonOperationPhase.BeforeExistingTargetBackup);
                ReportProgress(progress, addon.Name, operationName, InstallProgressStage.Moving, "Backing up existing addon...");
                Directory.Move(paths.TargetAddonDirectory, paths.BackupAddonDirectory);
                _journalStore.SetPhase(journal, AddonOperationPhase.AfterExistingTargetBackup);
                backupMoved = true;
            }
            else
            {
                commitStarted = true;
            }

            _journalStore.SetPhase(journal, AddonOperationPhase.BeforeStagedTargetMove);

            try
            {
                ReportProgress(progress, addon.Name, operationName, InstallProgressStage.Moving, "Moving new version to target...");
                Directory.Move(paths.StagedAddonDirectory, paths.TargetAddonDirectory);
            }
            catch
            {
                if (backupMoved && !Directory.Exists(paths.TargetAddonDirectory))
                    Directory.Move(paths.BackupAddonDirectory, paths.TargetAddonDirectory);

                throw;
            }

            _journalStore.SetPhase(journal, AddonOperationPhase.AfterStagedTargetMove);

            addon.MarkInstalled(installationMethod);

            ReportProgress(progress, addon.Name, operationName, InstallProgressStage.Committing, "Committing...");
            _journalStore.SetPhase(journal, AddonOperationPhase.BeforeDatabaseCommit);
            _addonDataManager.InsertOrUpdateLocalAddon(addon);

            _journalStore.Complete(journal);

            Debug.WriteLine($"{operationName} of addon {addon.Name} succeeded!");

            if (cancellationToken.IsCancellationRequested)
            {
                ReportProgress(progress, addon.Name, operationName, InstallProgressStage.Completed,
                    $"{operationName} completed after cancellation was requested.");
                return InstallResult.Success(
                    $"{operationName} of '{addon.Name}' completed after cancellation was requested.",
                    completedAfterCancellation: true);
            }

            ReportProgress(progress, addon.Name, operationName, InstallProgressStage.Completed,
                $"Addon {addon.Name} {operationName} completed successfully.");

            return InstallResult.Success();
        }
        catch (OperationCanceledException ex) when (!commitStarted)
        {
            Debug.WriteLine($"{operationName} of {addon.Name} was canceled before commit.");

            try
            {
                RestoreMetadata(addon, oldState, oldInstallationMethod, oldVersion, oldDisplayedVersion);

                if (journal != null)
                    _journalStore.CancelRolledBack(
                        journal,
                        $"{operationName} of '{addon.Name}' was canceled before commit and rolled back.");
            }
            catch (Exception rollbackEx)
            {
                preserveOperationRoot = true;

                if (journal != null)
                    _journalStore.MarkRecoveryFailed(
                        journal, rollbackEx.Message,
                        $"{operationName} of '{addon.Name}' was canceled, but rollback failed.");
            }

            return InstallResult.Canceled(
                $"{operationName} of addon {addon.Name} was canceled before commit and rolled back.",
                ex);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);

            var errorMessage = $"Failed to {operationName} addon {addon.Name}: {ex.Message}";

            if (journal != null)
                _journalStore.SetPhaseWithError(journal, journal.Phase, errorMessage);

            if (paths != null &&
                backupMoved &&
                !Directory.Exists(paths.TargetAddonDirectory) &&
                Directory.Exists(paths.BackupAddonDirectory))
                try
                {
                    ReportProgress(progress, addon.Name, operationName, InstallProgressStage.RollingBack, "Restoring backup...");
                    Directory.Move(paths.BackupAddonDirectory, paths.TargetAddonDirectory);

                    RestoreMetadata(addon, oldState, oldInstallationMethod, oldVersion, oldDisplayedVersion);

                    if (journal != null)
                        _journalStore.FailRolledBack(journal, errorMessage);

                    return InstallResult.Failure($"Failed to {operationName} addon {addon.Name}. Backup restored.", ex);
                }
                catch (Exception restoreEx)
                {
                    Debug.WriteLine(restoreEx);

                    preserveOperationRoot = true;
                    addon.State = AddonState.InstallationError;

                    if (journal != null)
                        _journalStore.MarkRecoveryFailed(
                            journal, restoreEx.Message,
                            $"Failed to {operationName} addon '{addon.Name}', and restoring backup also failed. " +
                            $"Backup preserved at: {paths.BackupAddonDirectory}");

                    return InstallResult.Failure(
                        $"Failed to replace addon {addon.Name}, and restoring backup also failed.",
                        restoreEx,
                        paths.BackupAddonDirectory);
                }

            RestoreMetadata(addon, oldState, oldInstallationMethod, oldVersion, oldDisplayedVersion);

            return InstallResult.Failure($"Failed to replace addon {addon.Name}.", ex,
                paths?.BackupAddonDirectory);
        }
        finally
        {
            if (paths != null && !preserveOperationRoot && journal is { IsComplete: true })
                TryDeleteDirectoryIfExists(paths.RootDirectory, out _);

            _addonDataManager.InsertOrUpdateLocalAddon(addon);
        }
    }

    // ---- Delete core ----

    private InstallResult DeleteCore(
        Addon addon,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        Debug.WriteLine($"Deleting addon {addon.Name}");

        ValidateAddonFolderName(addon.Name);

        var addonsRoot = GetSafeAddonsRoot();
        var addonPath = Path.GetFullPath(Path.Combine(addonsRoot, addon.Name));

        if (!IsPathInsideDirectory(addonPath, addonsRoot))
            return InstallResult.Failure($"Refusing to delete path outside AddOns directory: {addonPath}");

        var oldState = addon.State;
        var oldInstallationMethod = addon.InstallationMethod;
        var oldVersion = addon.Version;
        var oldDisplayedVersion = addon.DisplayedVersion;
        var hadLocalAddon = addon.LocalAddonId != null || addon.State != AddonState.NotInstalled;

        var paths = CreateOperationPaths(addon, AddonOperationType.Delete);

        var journal = _journalStore.Begin(
            addon, AddonOperationType.Delete, paths,
            oldState, oldInstallationMethod, oldVersion, oldDisplayedVersion,
            hadLocalAddon, addon.InstallationMethod);

        var commitStarted = false;
        var preserveOperationRoot = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReportProgress(progress, addon.Name, AddonOperationType.Delete, InstallProgressStage.Preparing,
                $"Preparing to delete {addon.Name}...");

            if (Directory.Exists(paths.TargetAddonDirectory))
            {
                commitStarted = true;
                _journalStore.SetPhase(journal, AddonOperationPhase.BeforeTargetBackupForDelete);
                ReportProgress(progress, addon.Name, AddonOperationType.Delete, InstallProgressStage.Moving,
                    $"Backing up {addon.Name} before deletion...");
                Directory.Move(paths.TargetAddonDirectory, paths.BackupAddonDirectory);
                _journalStore.SetPhase(journal, AddonOperationPhase.AfterTargetBackupForDelete);
            }
            else
            {
                commitStarted = true;
            }

            cancellationToken.ThrowIfCancellationRequested();

            ReportProgress(progress, addon.Name, AddonOperationType.Delete, InstallProgressStage.Committing,
                $"Removing {addon.Name} from database...");
            _journalStore.SetPhase(journal, AddonOperationPhase.BeforeDatabaseCommit);
            _addonDataManager.RemoveLocalAddon(addon);
            addon.LocalAddonId = null;
            addon.State = AddonState.NotInstalled;

            _journalStore.Complete(journal);

            ReportProgress(progress, addon.Name, AddonOperationType.Delete, InstallProgressStage.Completed,
                $"Addon {addon.Name} deleted successfully.");

            if (cancellationToken.IsCancellationRequested)
                return InstallResult.Success(
                    $"Delete of '{addon.Name}' completed after cancellation was requested.",
                    completedAfterCancellation: true);

            return InstallResult.Success();
        }
        catch (OperationCanceledException ex) when (!commitStarted)
        {
            Debug.WriteLine($"Delete of {addon.Name} was canceled before commit.");

            try
            {
                if (journal != null)
                    _journalStore.CancelRolledBack(
                        journal,
                        $"Delete of '{addon.Name}' was canceled before commit and rolled back.");
            }
            catch (Exception rollbackEx)
            {
                preserveOperationRoot = true;

                if (journal != null)
                    _journalStore.MarkRecoveryFailed(
                        journal, rollbackEx.Message,
                        $"Delete of '{addon.Name}' was canceled, but rollback failed.");
            }

            return InstallResult.Canceled(
                $"Delete of addon {addon.Name} was canceled before commit and rolled back.",
                ex);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Delete failed for {addon.Name}: {ex}");

            var errorMessage = $"Failed to delete addon {addon.Name}: {ex.Message}";

            if (journal != null)
                _journalStore.SetPhaseWithError(journal, journal.Phase, errorMessage);

            if (Directory.Exists(paths.BackupAddonDirectory) && !Directory.Exists(paths.TargetAddonDirectory))
                try
                {
                    ReportProgress(progress, addon.Name, AddonOperationType.Delete, InstallProgressStage.RollingBack,
                        "Restoring backup...");
                    Directory.Move(paths.BackupAddonDirectory, paths.TargetAddonDirectory);

                    if (journal != null)
                        _journalStore.FailRolledBack(journal, $"Delete failed, backup restored: {errorMessage}");

                    return InstallResult.Failure(
                        $"Failed to delete addon {addon.Name}. Backup restored.", ex);
                }
                catch (Exception restoreEx)
                {
                    preserveOperationRoot = true;

                    if (journal != null)
                        _journalStore.MarkRecoveryFailed(
                            journal, restoreEx.Message,
                            $"Delete of '{addon.Name}' failed and backup could not be restored. " +
                            $"Backup preserved at: {paths.BackupAddonDirectory}");

                    return InstallResult.Failure(
                        $"Failed to delete addon {addon.Name}, and restoring backup also failed.",
                        restoreEx,
                        paths.BackupAddonDirectory);
                }

            return InstallResult.Failure($"Failed to delete addon {addon.Name}.", ex);
        }
        finally
        {
            if (paths != null && !preserveOperationRoot && journal is { IsComplete: true })
                TryDeleteDirectoryIfExists(paths.RootDirectory, out _);
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

    // ---- Download and extraction ----

    private async Task<string> DownloadAndExtractToStagingAsync(
        Addon addon,
        Addon.AddonOperationPaths paths,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (addon.UniqueId == null)
            throw new InvalidOperationException($"Addon {addon.Name} has no unique online id.");

        ReportProgress(progress, addon.Name, "download", InstallProgressStage.Downloading, $"Downloading {addon.Name}...");

        var archivePath = await _archiveDownloader.DownloadAddonArchive(
            addon.UniqueId.Value, paths.DownloadDirectory, cancellationToken);

        if (string.IsNullOrWhiteSpace(archivePath))
            throw new InvalidOperationException($"Failed to download archive for addon {addon.Name}.");

        cancellationToken.ThrowIfCancellationRequested();

        ReportProgress(progress, addon.Name, "download", InstallProgressStage.Extracting, $"Extracting {addon.Name}...");

        ExtractArchiveToStaging(archivePath, paths.StagingDirectory, addon.Name);

        if (!Directory.Exists(paths.StagedAddonDirectory))
            throw new InvalidDataException($"Archive did not contain expected addon folder: {addon.Name}");

        var manifestPath = Path.Combine(paths.StagedAddonDirectory, $"{addon.Name}.txt");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"Archive did not contain expected addon manifest: {addon.Name}.txt");

        return paths.StagedAddonDirectory;
    }

    // ---- Archive validation and extraction ----

    private static string GetValidatedArchiveDestination(
        string? entryKey,
        string stagingDirectory,
        string expectedAddonFolder)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
            throw new InvalidDataException("Archive entry has an empty path.");

        if (entryKey.IndexOf('\0') >= 0)
            throw new InvalidDataException($"Archive entry contains a null character: {entryKey}");

        if (entryKey.StartsWith('/') ||
            entryKey.StartsWith('\\') ||
            Path.IsPathRooted(entryKey) ||
            LooksLikeWindowsRootedPath(entryKey))
            throw new InvalidDataException($"Archive entry has a rooted path: {entryKey}");

        var segments = entryKey
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
            throw new InvalidDataException($"Archive entry has no usable path segments: {entryKey}");

        foreach (var segment in segments)
        {
            if (segment is "." or "..")
                throw new InvalidDataException($"Archive entry contains traversal segment: {entryKey}");

            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException($"Archive entry contains invalid filename characters: {entryKey}");
        }

        if (!string.Equals(segments[0], expectedAddonFolder, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Archive entry is outside expected addon folder '{expectedAddonFolder}': {entryKey}");

        var relativePath = Path.Combine(segments);
        var destinationPath = Path.GetFullPath(Path.Combine(stagingDirectory, relativePath));

        var expectedRoot = Path.GetFullPath(Path.Combine(stagingDirectory, expectedAddonFolder));

        if (!IsPathInsideDirectory(destinationPath, expectedRoot))
            throw new InvalidDataException($"Archive entry escapes expected addon folder: {entryKey}");

        return destinationPath;
    }

    private static void ExtractArchiveToStaging(
        string archivePath,
        string stagingDirectory,
        string expectedAddonFolder)
    {
        using var archive = ArchiveFactory.Open(archivePath);

        var entries = archive.Entries
            .Where(entry => !entry.IsDirectory)
            .Select(entry => new
            {
                Entry = entry,
                DestinationPath = GetValidatedArchiveDestination(
                    entry.Key,
                    stagingDirectory,
                    expectedAddonFolder)
            })
            .ToList();

        if (entries.Count == 0)
            throw new InvalidDataException("Archive contains no files.");

        var duplicateCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            if (!duplicateCheck.Add(entry.DestinationPath))
                throw new InvalidDataException($"Archive contains duplicate destination path: {entry.DestinationPath}");

        foreach (var entry in entries)
        {
            var destinationDirectory = Path.GetDirectoryName(entry.DestinationPath);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
                throw new InvalidDataException($"Invalid archive destination path: {entry.DestinationPath}");

            Directory.CreateDirectory(destinationDirectory);

            using var input = entry.Entry.OpenEntryStream();
            using var output = new FileStream(
                entry.DestinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            input.CopyTo(output);
        }
    }

    // ---- Paths and helpers ----

    private static string GetSafeAddonsRoot()
    {
        var addonsRoot = AppSettings.Instance.AddonsDirectory;
        if (string.IsNullOrWhiteSpace(addonsRoot))
            throw new InvalidOperationException("AddOns directory is not configured.");

        var fullPath = Path.GetFullPath(addonsRoot);
        Directory.CreateDirectory(fullPath);

        return fullPath;
    }

    private static void ValidateAddonFolderName(string addonName)
    {
        if (string.IsNullOrWhiteSpace(addonName))
            throw new InvalidOperationException("Addon name is empty.");

        if (addonName is "." or "..")
            throw new InvalidOperationException($"Unsafe addon folder name: {addonName}");

        if (addonName.Contains(Path.DirectorySeparatorChar) ||
            addonName.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidOperationException($"Addon name must be a folder name, not a path: {addonName}");

        if (addonName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException($"Addon name contains invalid path characters: {addonName}");
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        var relativePath = Path.GetRelativePath(directory, path);

        return relativePath == "." ||
               (!relativePath.StartsWith("..", StringComparison.Ordinal) &&
                !Path.IsPathRooted(relativePath));
    }

    private static bool LooksLikeWindowsRootedPath(string path)
    {
        return path.Length >= 2 &&
               char.IsLetter(path[0]) &&
               path[1] == ':';
    }

    private Addon.AddonOperationPaths CreateOperationPaths(Addon addon, string operationName)
    {
        var addonsRoot = GetSafeAddonsRoot();
        ValidateAddonFolderName(addon.Name);

        var addonsParent = Directory.GetParent(addonsRoot)?.FullName;
        if (string.IsNullOrWhiteSpace(addonsParent))
            throw new InvalidOperationException($"AddOns directory has no parent directory: {addonsRoot}");

        var operationId = Guid.NewGuid().ToString("N");
        var operationsRoot = Path.Combine(addonsParent, ".SpellCrafterOperations");
        var operationRoot = Path.Combine(
            operationsRoot,
            $"{operationName}-{addon.Name}-{operationId}");

        var downloadDirectory = Path.Combine(operationRoot, "download");
        var stagingDirectory = Path.Combine(operationRoot, "staging");
        var backupDirectory = Path.Combine(operationRoot, "backup");

        Directory.CreateDirectory(downloadDirectory);
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(backupDirectory);

        var targetAddonDirectory = Path.Combine(addonsRoot, addon.Name);
        var stagedAddonDirectory = Path.Combine(stagingDirectory, addon.Name);
        var backupAddonDirectory = Path.Combine(backupDirectory, addon.Name);

        return new Addon.AddonOperationPaths(
            operationId,
            addonsRoot,
            operationsRoot,
            operationRoot,
            downloadDirectory,
            stagingDirectory,
            backupDirectory,
            targetAddonDirectory,
            stagedAddonDirectory,
            backupAddonDirectory);
    }

    private static bool TryDeleteDirectoryIfExists(string path, out Exception? error)
    {
        error = null;

        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);

            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private static void ReportProgress(
        IProgress<InstallProgress>? progress,
        string addonName,
        string operation,
        InstallProgressStage stage,
        string message)
    {
        progress?.Report(new InstallProgress(addonName, operation, stage, message));
    }

    private bool TryRollbackInstall(
        Addon addon,
        AddonOperationJournal? journal,
        AddonState oldState,
        AddonInstallationMethod oldInstallationMethod,
        string oldVersion,
        string oldDisplayedVersion,
        bool hadLocalAddon)
    {
        try
        {
            RestoreMetadata(addon, oldState, oldInstallationMethod, oldVersion, oldDisplayedVersion);

            if (!hadLocalAddon)
            {
                _addonDataManager.RemoveLocalAddon(addon);
                addon.LocalAddonId = null;
                addon.State = AddonState.NotInstalled;
            }

            if (journal != null)
                _journalStore.CancelRolledBack(
                    journal,
                    $"Installation of '{addon.Name}' was canceled before commit and rolled back.");

            return true;
        }
        catch (Exception rollbackEx)
        {
            if (journal != null)
                _journalStore.MarkRecoveryFailed(
                    journal, rollbackEx.Message,
                    $"Installation of '{addon.Name}' was canceled, but rollback failed. Manual review required.");

            return false;
        }
    }

    private static void RestoreMetadata(
        Addon addon,
        AddonState state,
        AddonInstallationMethod installationMethod,
        string version,
        string displayedVersion)
    {
        addon.State = state;
        addon.InstallationMethod = installationMethod;
        addon.Version = version;
        addon.DisplayedVersion = displayedVersion;
    }
}
