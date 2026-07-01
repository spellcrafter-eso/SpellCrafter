using System.Collections.Generic;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

public interface IAddonOperationJournalStore
{
    AddonOperationJournal Begin(
        Addon addon,
        string operationType,
        Addon.AddonOperationPaths paths,
        AddonState previousState,
        AddonInstallationMethod previousInstallationMethod,
        string previousVersion,
        string previousDisplayedVersion,
        bool previousHadLocalAddon,
        AddonInstallationMethod targetInstallationMethod);

    void SetPhase(
        AddonOperationJournal journal,
        string phase);

    void SetPhaseWithError(
        AddonOperationJournal journal,
        string phase,
        string errorMessage);

    void Complete(
        AddonOperationJournal journal,
        string phase = AddonOperationPhase.Committed);

    void CompleteWithAttention(
        AddonOperationJournal journal,
        string phase,
        string recoveryMessage);

    void FailRolledBack(
        AddonOperationJournal journal,
        string errorMessage);

    void CancelRolledBack(
        AddonOperationJournal journal,
        string recoveryMessage);

    void MarkRequiresAttention(
        AddonOperationJournal journal,
        string recoveryMessage,
        string? errorMessage = null);

    void MarkRecoveryFailed(
        AddonOperationJournal journal,
        string errorMessage,
        string recoveryMessage);

    List<AddonOperationJournal> GetIncompleteOperations();

    List<AddonOperationJournal> GetAttentionOperations();
}