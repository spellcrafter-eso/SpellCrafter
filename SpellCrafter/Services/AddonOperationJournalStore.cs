using System;
using System.Collections.Generic;
using System.Linq;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

internal static class AddonOperationJournalStore
{
    public static AddonOperationJournal Begin(
        EsoDataConnection db,
        Addon addon,
        string operationType,
        Addon.AddonOperationPaths paths,
        AddonState previousState,
        AddonInstallationMethod previousInstallationMethod,
        string previousVersion,
        string previousDisplayedVersion,
        bool previousHadLocalAddon,
        AddonInstallationMethod targetInstallationMethod)
    {
        var now = DateTime.UtcNow;

        var journal = new AddonOperationJournal
        {
            OperationId = paths.OperationId,
            OperationType = operationType,
            Phase = AddonOperationPhase.Created,
            IsComplete = false,
            RequiresAttention = false,
            CommonAddonId = addon.CommonAddonId,
            UniqueId = addon.UniqueId,
            AddonName = addon.Name,
            PreviousHadLocalAddon = previousHadLocalAddon,
            PreviousState = previousState,
            PreviousInstallationMethod = previousInstallationMethod,
            PreviousVersion = previousVersion,
            PreviousDisplayedVersion = previousDisplayedVersion,
            TargetInstallationMethod = targetInstallationMethod,
            TargetVersion = addon.LatestVersion,
            TargetDisplayedVersion = addon.DisplayedLatestVersion,
            AddonsRootDirectory = paths.AddonsRootDirectory,
            OperationRootDirectory = paths.RootDirectory,
            DownloadDirectory = paths.DownloadDirectory,
            StagingDirectory = paths.StagingDirectory,
            BackupDirectory = paths.BackupDirectory,
            TargetAddonDirectory = paths.TargetAddonDirectory,
            StagedAddonDirectory = paths.StagedAddonDirectory,
            BackupAddonDirectory = paths.BackupAddonDirectory,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Insert(journal);
        return journal;
    }

    public static void SetPhase(
        EsoDataConnection db,
        AddonOperationJournal journal,
        string phase)
    {
        journal.Phase = phase;
        journal.UpdatedAtUtc = DateTime.UtcNow;
        db.Update(journal);
    }

    public static void SetPhaseWithError(
        EsoDataConnection db,
        AddonOperationJournal journal,
        string phase,
        string errorMessage)
    {
        journal.Phase = phase;
        journal.UpdatedAtUtc = DateTime.UtcNow;
        journal.LastErrorMessage = errorMessage;
        db.Update(journal);
    }

    public static void Complete(
        EsoDataConnection db,
        AddonOperationJournal journal,
        string phase = AddonOperationPhase.Committed)
    {
        journal.Phase = phase;
        journal.IsComplete = true;
        journal.UpdatedAtUtc = DateTime.UtcNow;
        journal.CompletedAtUtc = DateTime.UtcNow;
        db.Update(journal);
    }

    public static void CompleteWithAttention(
        EsoDataConnection db,
        AddonOperationJournal journal,
        string phase,
        string recoveryMessage)
    {
        journal.Phase = phase;
        journal.IsComplete = true;
        journal.RequiresAttention = true;
        journal.RecoveryMessage = recoveryMessage;
        journal.UpdatedAtUtc = DateTime.UtcNow;
        journal.CompletedAtUtc = DateTime.UtcNow;
        db.Update(journal);
    }

    public static void FailRolledBack(
        EsoDataConnection db,
        AddonOperationJournal journal,
        string errorMessage)
    {
        journal.Phase = AddonOperationPhase.FailedRolledBack;
        journal.IsComplete = true;
        journal.LastErrorMessage = errorMessage;
        journal.UpdatedAtUtc = DateTime.UtcNow;
        journal.CompletedAtUtc = DateTime.UtcNow;
        db.Update(journal);
    }

    public static void CancelRolledBack(
        EsoDataConnection db,
        AddonOperationJournal journal,
        string recoveryMessage)
    {
        journal.Phase = AddonOperationPhase.CanceledRolledBack;
        journal.IsComplete = true;
        journal.RequiresAttention = false;
        journal.RecoveryMessage = recoveryMessage;
        journal.UpdatedAtUtc = DateTime.UtcNow;
        journal.CompletedAtUtc = DateTime.UtcNow;
        db.Update(journal);
    }

    public static void MarkRequiresAttention(
        EsoDataConnection db,
        AddonOperationJournal journal,
        string recoveryMessage,
        string? errorMessage = null)
    {
        journal.RequiresAttention = true;
        journal.RecoveryMessage = recoveryMessage;

        if (!string.IsNullOrWhiteSpace(errorMessage))
            journal.LastErrorMessage = errorMessage;

        journal.UpdatedAtUtc = DateTime.UtcNow;
        db.Update(journal);
    }

    public static void MarkRecoveryFailed(
        EsoDataConnection db,
        AddonOperationJournal journal,
        string errorMessage,
        string recoveryMessage)
    {
        journal.Phase = AddonOperationPhase.RecoveryFailed;
        journal.LastErrorMessage = errorMessage;
        journal.RecoveryMessage = recoveryMessage;
        journal.RequiresAttention = true;
        journal.UpdatedAtUtc = DateTime.UtcNow;
        journal.CompletedAtUtc = DateTime.UtcNow;
        journal.IsComplete = true;
        db.Update(journal);
    }

    public static List<AddonOperationJournal> GetIncompleteOperations(EsoDataConnection db)
    {
        return db.Table<AddonOperationJournal>()
            .Where(j => !j.IsComplete)
            .OrderBy(j => j.CreatedAtUtc)
            .ToList();
    }

    public static List<AddonOperationJournal> GetAttentionOperations(EsoDataConnection db)
    {
        return db.Table<AddonOperationJournal>()
            .Where(j => j.RequiresAttention)
            .OrderBy(j => j.CreatedAtUtc)
            .ToList();
    }
}