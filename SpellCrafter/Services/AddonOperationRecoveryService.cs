using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

public class AddonOperationRecoveryService
{
    private readonly IAddonOperationJournalStore _journalStore;

    public AddonOperationRecoveryService(IAddonOperationJournalStore journalStore)
    {
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
    }

    public static RangedObservableCollection<OperationRecoveryNotice> Notices { get; } = [];

    public void RecoverIncompleteOperations(EsoDataConnection db)
    {
        Notices.Clear();

        var incomplete = _journalStore.GetIncompleteOperations();
        var attention = _journalStore.GetAttentionOperations();

        foreach (var journal in attention)
            Notices.Add(new OperationRecoveryNotice
            {
                Type = MessageDialogType.Warning,
                Message = $"Previous operation for '{journal.AddonName}' needs attention: {journal.RecoveryMessage}",
                Details = journal.LastErrorMessage,
                BackupPath = journal.BackupAddonDirectory
            });

        foreach (var journal in incomplete)
            try
            {
                if (!ValidateJournalPaths(journal))
                {
                    _journalStore.MarkRecoveryFailed(
                        journal,
                        "Journal paths failed validation.",
                        $"Stored paths for '{journal.AddonName}' operation are invalid or unsafe. " +
                        "SpellCrafter did not modify any files. Check the operation root directory.");
                    continue;
                }

                // Save the interrupted phase BEFORE overwriting with RecoveryInProgress
                var interruptedPhase = journal.Phase;

                _journalStore.SetPhase(journal, AddonOperationPhase.RecoveryInProgress);

                switch (journal.OperationType)
                {
                    case AddonOperationType.Install:
                        RecoverInstall(db, journal, interruptedPhase);
                        break;
                    case AddonOperationType.Update:
                    case AddonOperationType.Reinstall:
                        RecoverReplacement(db, journal, interruptedPhase);
                        break;
                    case AddonOperationType.Delete:
                        RecoverDelete(db, journal, interruptedPhase);
                        break;
                    default:
                        _journalStore.MarkRecoveryFailed(
                            journal,
                            $"Unknown operation type: {journal.OperationType}",
                            $"Unknown operation type '{journal.OperationType}' for addon '{journal.AddonName}'. " +
                            "No automatic recovery was attempted.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Recovery failed for addon {journal.AddonName}: {ex}");

                _journalStore.MarkRecoveryFailed(
                    journal,
                    ex.Message,
                    $"SpellCrafter could not automatically recover an interrupted {journal.OperationType} " +
                    $"operation for '{journal.AddonName}'. User files were preserved where possible.");
            }
    }

    private static bool ValidateJournalPaths(AddonOperationJournal journal)
    {
        if (string.IsNullOrWhiteSpace(journal.AddonsRootDirectory))
            return false;

        if (string.IsNullOrWhiteSpace(journal.OperationRootDirectory))
            return false;

        if (string.IsNullOrWhiteSpace(journal.TargetAddonDirectory))
            return false;

        if (string.IsNullOrWhiteSpace(journal.AddonName))
            return false;

        if (journal.AddonName is "." or "..")
            return false;

        if (journal.AddonName.Contains(Path.DirectorySeparatorChar) ||
            journal.AddonName.Contains(Path.AltDirectorySeparatorChar))
            return false;

        if (journal.AddonName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        try
        {
            var normalizedAddonsRoot = Path.GetFullPath(journal.AddonsRootDirectory);
            var normalizedTarget = Path.GetFullPath(journal.TargetAddonDirectory);

            if (!normalizedTarget.StartsWith(normalizedAddonsRoot, StringComparison.Ordinal))
                return false;

            if (!string.IsNullOrWhiteSpace(journal.OperationRootDirectory))
            {
                var normalizedOpRoot = Path.GetFullPath(journal.OperationRootDirectory);
                var opParent = Directory.GetParent(normalizedOpRoot)?.Parent?.FullName;

                if (opParent != null && !normalizedAddonsRoot.StartsWith(opParent, StringComparison.Ordinal) &&
                    !opParent.StartsWith(normalizedAddonsRoot, StringComparison.Ordinal))
                {
                    var opsDirName = Path.GetFileName(Path.GetDirectoryName(journal.OperationRootDirectory) ?? "");
                    if (!string.Equals(opsDirName, ".SpellCrafterOperations", StringComparison.Ordinal))
                        return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        try
        {
            var relativePath = Path.GetRelativePath(directory, path);
            return relativePath == "." ||
                   (!relativePath.StartsWith("..", StringComparison.Ordinal) &&
                    !Path.IsPathRooted(relativePath));
        }
        catch
        {
            return false;
        }
    }

    private void RecoverInstall(EsoDataConnection db, AddonOperationJournal journal, string interruptedPhase)
    {
        var targetExists = Directory.Exists(journal.TargetAddonDirectory);
        var stagedExists = Directory.Exists(journal.StagedAddonDirectory);

        var phasesBeforeTargetMove = new HashSet<string>
        {
            AddonOperationPhase.Created,
            AddonOperationPhase.MarkingLocalAddonInstalling,
            AddonOperationPhase.ExtractingToStaging,
            AddonOperationPhase.StagingReady,
            AddonOperationPhase.BeforeStagedTargetMove
        };

        if (phasesBeforeTargetMove.Contains(interruptedPhase))
        {
            if (!targetExists)
            {
                CleanupOperationDirectory(journal);

                if (journal.PreviousHadLocalAddon)
                    RestorePreviousLocalMetadata(db, journal);
                else
                    RemoveLocalAddonRow(db, journal);

                _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);

                Notices.Add(new OperationRecoveryNotice
                {
                    Type = MessageDialogType.Info,
                    Message = $"Installation of '{journal.AddonName}' was rolled back after interruption."
                });
            }
            else
            {
                _journalStore.MarkRecoveryFailed(
                    journal,
                    $"Target directory exists at phase '{interruptedPhase}': {journal.TargetAddonDirectory}",
                    $"The addon '{journal.AddonName}' appears to be installed, but the operation journal " +
                    "says it was not yet moved. Manual review recommended.");
            }
        }
        else if (interruptedPhase is AddonOperationPhase.AfterStagedTargetMove or AddonOperationPhase.BeforeDatabaseCommit)
        {
            if (targetExists)
            {
                CommitInstalledMetadata(db, journal);
                CleanupOperationDirectory(journal);

                _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);

                Notices.Add(new OperationRecoveryNotice
                {
                    Type = MessageDialogType.Info,
                    Message = $"Installation of '{journal.AddonName}' was completed after interruption."
                });
            }
            else
            {
                _journalStore.MarkRecoveryFailed(
                    journal,
                    $"Target directory missing after staged move: {journal.TargetAddonDirectory}",
                    $"The addon '{journal.AddonName}' was partially installed but the target folder is missing. " +
                    "Reinstall the addon manually.");
            }
        }
        else
        {
            _journalStore.MarkRecoveryFailed(
                journal,
                $"Unrecognized phase for install: {interruptedPhase}",
                $"Unrecognized phase '{interruptedPhase}' for install of '{journal.AddonName}'.");
        }
    }

    private void RecoverReplacement(EsoDataConnection db, AddonOperationJournal journal, string interruptedPhase)
    {
        var targetExists = Directory.Exists(journal.TargetAddonDirectory);
        var backupExists = Directory.Exists(journal.BackupAddonDirectory);
        var stagedExists = Directory.Exists(journal.StagedAddonDirectory);

        var phasesBeforeBackup = new HashSet<string>
        {
            AddonOperationPhase.Created,
            AddonOperationPhase.MarkingLocalAddonInstalling,
            AddonOperationPhase.ExtractingToStaging,
            AddonOperationPhase.StagingReady
        };

        if (phasesBeforeBackup.Contains(interruptedPhase))
        {
            RestorePreviousLocalMetadata(db, journal);
            CleanupOperationDirectory(journal);

            _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);

            Notices.Add(new OperationRecoveryNotice
            {
                Type = MessageDialogType.Info,
                Message = $"Update/reinstall of '{journal.AddonName}' was rolled back after interruption."
            });
        }
        else if (interruptedPhase is AddonOperationPhase.BeforeExistingTargetBackup)
        {
            if (targetExists)
            {
                RestorePreviousLocalMetadata(db, journal);
                CleanupOperationDirectory(journal);

                _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);

                Notices.Add(new OperationRecoveryNotice
                {
                    Type = MessageDialogType.Info,
                    Message = $"Update/reinstall of '{journal.AddonName}' was rolled back after interruption."
                });
            }
            else
            {
                _journalStore.MarkRecoveryFailed(
                    journal,
                    $"Target missing before backup at phase {interruptedPhase}",
                    $"The addon '{journal.AddonName}' target folder is missing but the journal says " +
                    "backup had not started. Manual review required.");
            }
        }
        else if (interruptedPhase == AddonOperationPhase.AfterExistingTargetBackup)
        {
            if (!targetExists && backupExists)
                try
                {
                    Directory.Move(journal.BackupAddonDirectory, journal.TargetAddonDirectory);
                    RestorePreviousLocalMetadata(db, journal);
                    CleanupOperationDirectory(journal);

                    _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);

                    Notices.Add(new OperationRecoveryNotice
                    {
                        Type = MessageDialogType.Info,
                        Message = $"Update/reinstall of '{journal.AddonName}' was rolled back. " +
                                  "The previous version was restored."
                    });
                }
                catch (Exception ex)
                {
                    _journalStore.MarkRecoveryFailed(
                        journal, ex.Message,
                        $"Could not restore backup of '{journal.AddonName}' during recovery. " +
                        $"The backup may still exist at: {journal.BackupAddonDirectory}");
                }
            else if (targetExists)
                _journalStore.MarkRecoveryFailed(
                    journal,
                    $"Target exists after backup at phase {interruptedPhase}",
                    $"Both target and backup directories exist for '{journal.AddonName}'. " +
                    "SpellCrafter preserved both and requires manual review.");
            else
                _journalStore.MarkRecoveryFailed(
                    journal,
                    $"Both target and backup missing at phase {interruptedPhase}",
                    $"Both target and backup directories for '{journal.AddonName}' are missing. " +
                    "Reinstall the addon manually.");
        }
        else if (interruptedPhase is AddonOperationPhase.BeforeStagedTargetMove or AddonOperationPhase.AfterStagedTargetMove)
        {
            if (interruptedPhase == AddonOperationPhase.BeforeStagedTargetMove)
            {
                if (backupExists && !targetExists)
                {
                    try
                    {
                        Directory.Move(journal.BackupAddonDirectory, journal.TargetAddonDirectory);
                        RestorePreviousLocalMetadata(db, journal);
                        CleanupOperationDirectory(journal);

                        _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);

                        Notices.Add(new OperationRecoveryNotice
                        {
                            Type = MessageDialogType.Info,
                            Message = $"Update/reinstall of '{journal.AddonName}' was rolled back. " +
                                      "The previous version was restored."
                        });
                    }
                    catch (Exception ex)
                    {
                        _journalStore.MarkRecoveryFailed(
                            journal, ex.Message,
                            $"Could not restore backup of '{journal.AddonName}' during recovery.");
                    }
                }
                else if (targetExists)
                {
                    _journalStore.MarkRecoveryFailed(
                        journal,
                        $"Target exists at phase {interruptedPhase}",
                        $"The addon '{journal.AddonName}' target exists but the journal says staged move " +
                        "had not occurred yet. Manual review required.");
                }
                else
                {
                    RestorePreviousLocalMetadata(db, journal);
                    CleanupOperationDirectory(journal);
                    _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);

                    Notices.Add(new OperationRecoveryNotice
                    {
                        Type = MessageDialogType.Warning,
                        Message = $"Update/reinstall of '{journal.AddonName}' could not restore the previous " +
                                  "version. The addon has been marked as not installed."
                    });
                }
            }
            else
            {
                if (targetExists)
                {
                    CommitInstalledMetadata(db, journal);

                    if (backupExists)
                    {
                        _journalStore.CompleteWithAttention(
                            journal,
                            AddonOperationPhase.RecoveryCompleted,
                            $"The update/reinstall of '{journal.AddonName}' was completed. " +
                            $"A backup of the previous version was preserved at: {journal.BackupAddonDirectory}");

                        Notices.Add(new OperationRecoveryNotice
                        {
                            Type = MessageDialogType.Warning,
                            Message = $"Update/reinstall of '{journal.AddonName}' completed after interruption. " +
                                      "A backup of the previous version was preserved.",
                            BackupPath = journal.BackupAddonDirectory
                        });
                    }
                    else
                    {
                        CleanupOperationDirectory(journal);
                        _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);

                        Notices.Add(new OperationRecoveryNotice
                        {
                            Type = MessageDialogType.Info,
                            Message = $"Update/reinstall of '{journal.AddonName}' was completed after interruption."
                        });
                    }
                }
                else if (backupExists && !targetExists)
                {
                    try
                    {
                        Directory.Move(journal.BackupAddonDirectory, journal.TargetAddonDirectory);
                        RestorePreviousLocalMetadata(db, journal);
                        CleanupOperationDirectory(journal);

                        _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);

                        Notices.Add(new OperationRecoveryNotice
                        {
                            Type = MessageDialogType.Info,
                            Message = $"Update/reinstall of '{journal.AddonName}' was rolled back. " +
                                      "The previous version was restored."
                        });
                    }
                    catch (Exception ex)
                    {
                        _journalStore.MarkRecoveryFailed(
                            journal, ex.Message,
                            $"Could not restore backup of '{journal.AddonName}'. " +
                            $"Backup may still exist at: {journal.BackupAddonDirectory}");
                    }
                }
                else
                {
                    _journalStore.MarkRecoveryFailed(
                        journal,
                        $"Both target and backup missing after staged move",
                        $"Both target and backup directories for '{journal.AddonName}' are missing. " +
                        "Reinstall the addon manually.");
                }
            }
        }
        else if (interruptedPhase == AddonOperationPhase.BeforeDatabaseCommit)
        {
            if (targetExists)
            {
                CommitInstalledMetadata(db, journal);

                if (backupExists)
                {
                    _journalStore.CompleteWithAttention(
                        journal,
                        AddonOperationPhase.RecoveryCompleted,
                        $"The update/reinstall of '{journal.AddonName}' was completed. " +
                        $"The previous version backup was preserved at: {journal.BackupAddonDirectory}");

                    Notices.Add(new OperationRecoveryNotice
                    {
                        Type = MessageDialogType.Warning,
                        Message = $"Update/reinstall of '{journal.AddonName}' completed after interruption. " +
                                  "A backup of the previous version was preserved.",
                        BackupPath = journal.BackupAddonDirectory
                    });
                }
                else
                {
                    CleanupOperationDirectory(journal);
                    _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);

                    Notices.Add(new OperationRecoveryNotice
                    {
                        Type = MessageDialogType.Info,
                        Message = $"Update/reinstall of '{journal.AddonName}' was completed after interruption."
                    });
                }
            }
            else
            {
                _journalStore.MarkRecoveryFailed(
                    journal,
                    $"Target missing at phase {interruptedPhase}",
                    $"The addon '{journal.AddonName}' target directory is missing after the staged move. " +
                    "Update/reinstall could not be completed.");
            }
        }
        else
        {
            _journalStore.MarkRecoveryFailed(
                journal,
                $"Unrecognized phase for replacement: {interruptedPhase}",
                $"Unrecognized phase '{interruptedPhase}' for update/reinstall of '{journal.AddonName}'.");
        }
    }

    private void RecoverDelete(EsoDataConnection db, AddonOperationJournal journal, string interruptedPhase)
    {
        var targetExists = Directory.Exists(journal.TargetAddonDirectory);
        var backupExists = Directory.Exists(journal.BackupAddonDirectory);

        if (interruptedPhase is AddonOperationPhase.Created or AddonOperationPhase.BeforeTargetBackupForDelete)
        {
            RestorePreviousLocalMetadata(db, journal);
            CleanupOperationDirectory(journal);

            _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);

            Notices.Add(new OperationRecoveryNotice
            {
                Type = MessageDialogType.Info,
                Message = $"Delete of '{journal.AddonName}' was canceled/rolled back after interruption."
            });
        }
        else if (interruptedPhase is AddonOperationPhase.AfterTargetBackupForDelete or AddonOperationPhase.BeforeDatabaseCommit)
        {
            if (!targetExists && backupExists)
            {
                try
                {
                    Directory.Move(journal.BackupAddonDirectory, journal.TargetAddonDirectory);
                    RestorePreviousLocalMetadata(db, journal);
                    CleanupOperationDirectory(journal);

                    _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);

                    Notices.Add(new OperationRecoveryNotice
                    {
                        Type = MessageDialogType.Info,
                        Message = $"Delete of '{journal.AddonName}' was interrupted. The addon was restored."
                    });
                }
                catch (Exception ex)
                {
                    _journalStore.MarkRecoveryFailed(
                        journal, ex.Message,
                        $"Could not restore deleted addon '{journal.AddonName}' during recovery. " +
                        $"The backup may still exist at: {journal.BackupAddonDirectory}");
                }
            }
            else if (targetExists)
            {
                RestorePreviousLocalMetadata(db, journal);
                CleanupOperationDirectory(journal);

                _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);

                Notices.Add(new OperationRecoveryNotice
                {
                    Type = MessageDialogType.Warning,
                    Message = $"Delete of '{journal.AddonName}' was interrupted. " +
                              "The addon appears to still be installed."
                });
            }
            else
            {
                RemoveLocalAddonRow(db, journal);

                if (backupExists)
                {
                    _journalStore.CompleteWithAttention(
                        journal,
                        AddonOperationPhase.RecoveryCompleted,
                        $"The addon '{journal.AddonName}' was deleted. " +
                        $"A backup exists at: {journal.BackupAddonDirectory}");

                    Notices.Add(new OperationRecoveryNotice
                    {
                        Type = MessageDialogType.Warning,
                        Message = $"Delete of '{journal.AddonName}' was completed after interruption. " +
                                  "A backup of the addon was preserved.",
                        BackupPath = journal.BackupAddonDirectory
                    });
                }
                else
                {
                    CleanupOperationDirectory(journal);
                    _journalStore.Complete(journal, AddonOperationPhase.RecoveryCompleted);
                }
            }
        }
        else
        {
            _journalStore.MarkRecoveryFailed(
                journal,
                $"Unrecognized phase for delete: {interruptedPhase}",
                $"Unrecognized phase '{interruptedPhase}' for delete of '{journal.AddonName}'.");
        }
    }

    private static void RestorePreviousLocalMetadata(EsoDataConnection db, AddonOperationJournal journal)
    {
        if (journal.PreviousHadLocalAddon)
        {
            var existing = db.Table<LocalAddon>()
                .FirstOrDefault(la => la.CommonAddonId == journal.CommonAddonId);

            if (existing != null)
            {
                existing.Version = journal.PreviousVersion;
                existing.DisplayedVersion = journal.PreviousDisplayedVersion;
                existing.State = journal.PreviousState;
                existing.InstallationMethod = journal.PreviousInstallationMethod;
                db.Update(existing);
            }
            else
            {
                var localAddon = new LocalAddon
                {
                    CommonAddonId = journal.CommonAddonId,
                    Version = journal.PreviousVersion,
                    DisplayedVersion = journal.PreviousDisplayedVersion,
                    State = journal.PreviousState,
                    InstallationMethod = journal.PreviousInstallationMethod
                };
                db.Insert(localAddon);
            }
        }
        else
        {
            RemoveLocalAddonRow(db, journal);
        }
    }

    private static void CommitInstalledMetadata(EsoDataConnection db, AddonOperationJournal journal)
    {
        var existing = db.Table<LocalAddon>()
            .FirstOrDefault(la => la.CommonAddonId == journal.CommonAddonId);

        if (existing != null)
        {
            existing.Version = journal.TargetVersion;
            existing.DisplayedVersion = journal.TargetDisplayedVersion;
            existing.State = AddonState.LatestVersion;
            existing.InstallationMethod = journal.TargetInstallationMethod;
            db.Update(existing);
        }
        else
        {
            var localAddon = new LocalAddon
            {
                CommonAddonId = journal.CommonAddonId,
                Version = journal.TargetVersion,
                DisplayedVersion = journal.TargetDisplayedVersion,
                State = AddonState.LatestVersion,
                InstallationMethod = journal.TargetInstallationMethod
            };
            db.Insert(localAddon);
        }
    }

    private static void RemoveLocalAddonRow(EsoDataConnection db, AddonOperationJournal journal)
    {
        var existing = db.Table<LocalAddon>()
            .FirstOrDefault(la => la.CommonAddonId == journal.CommonAddonId);

        if (existing != null)
            db.Delete(existing);
    }

    private static void CleanupOperationDirectory(AddonOperationJournal journal)
    {
        if (!string.IsNullOrWhiteSpace(journal.OperationRootDirectory))
            try
            {
                if (Directory.Exists(journal.OperationRootDirectory))
                    Directory.Delete(journal.OperationRootDirectory, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to clean up operation directory: {journal.OperationRootDirectory}: {ex}");
            }
    }
}