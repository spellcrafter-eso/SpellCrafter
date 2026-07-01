using System;
using System.Collections.Generic;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

public sealed class AddonOperationJournalStoreAdapter : IAddonOperationJournalStore
{
    private readonly IEsoDataConnectionFactory _dbConnectionFactory;

    public AddonOperationJournalStoreAdapter()
        : this(new EsoDataConnectionFactory())
    {
    }

    public AddonOperationJournalStoreAdapter(IEsoDataConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory
                               ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
    }

    public AddonOperationJournal Begin(
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
        using var db = _dbConnectionFactory.CreateConnection();
        return AddonOperationJournalStore.Begin(
            db, addon, operationType, paths,
            previousState, previousInstallationMethod,
            previousVersion, previousDisplayedVersion,
            previousHadLocalAddon, targetInstallationMethod);
    }

    public void SetPhase(AddonOperationJournal journal, string phase)
    {
        using var db = _dbConnectionFactory.CreateConnection();
        AddonOperationJournalStore.SetPhase(db, journal, phase);
    }

    public void SetPhaseWithError(
        AddonOperationJournal journal,
        string phase,
        string errorMessage)
    {
        using var db = _dbConnectionFactory.CreateConnection();
        AddonOperationJournalStore.SetPhaseWithError(db, journal, phase, errorMessage);
    }

    public void Complete(
        AddonOperationJournal journal,
        string phase = AddonOperationPhase.Committed)
    {
        using var db = _dbConnectionFactory.CreateConnection();
        AddonOperationJournalStore.Complete(db, journal, phase);
    }

    public void CompleteWithAttention(
        AddonOperationJournal journal,
        string phase,
        string recoveryMessage)
    {
        using var db = _dbConnectionFactory.CreateConnection();
        AddonOperationJournalStore.CompleteWithAttention(db, journal, phase, recoveryMessage);
    }

    public void FailRolledBack(
        AddonOperationJournal journal,
        string errorMessage)
    {
        using var db = _dbConnectionFactory.CreateConnection();
        AddonOperationJournalStore.FailRolledBack(db, journal, errorMessage);
    }

    public void CancelRolledBack(
        AddonOperationJournal journal,
        string recoveryMessage)
    {
        using var db = _dbConnectionFactory.CreateConnection();
        AddonOperationJournalStore.CancelRolledBack(db, journal, recoveryMessage);
    }

    public void MarkRequiresAttention(
        AddonOperationJournal journal,
        string recoveryMessage,
        string? errorMessage = null)
    {
        using var db = _dbConnectionFactory.CreateConnection();
        AddonOperationJournalStore.MarkRequiresAttention(db, journal, recoveryMessage, errorMessage);
    }

    public void MarkRecoveryFailed(
        AddonOperationJournal journal,
        string errorMessage,
        string recoveryMessage)
    {
        using var db = _dbConnectionFactory.CreateConnection();
        AddonOperationJournalStore.MarkRecoveryFailed(db, journal, errorMessage, recoveryMessage);
    }

    public List<AddonOperationJournal> GetIncompleteOperations()
    {
        using var db = _dbConnectionFactory.CreateConnection();
        return AddonOperationJournalStore.GetIncompleteOperations(db);
    }

    public List<AddonOperationJournal> GetAttentionOperations()
    {
        using var db = _dbConnectionFactory.CreateConnection();
        return AddonOperationJournalStore.GetAttentionOperations(db);
    }
}