using System.Collections.Generic;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Tests.TestInfrastructure;

/// <summary>
/// Wraps an <see cref="IAddonOperationJournalStore"/> and invokes a configurable callback
/// whenever <see cref="SetPhase"/> is called. Useful for injecting failures or cancellations
/// at precise points during an operation. The callback receives the journal (which exposes
/// operation paths) and the new phase name.
/// </summary>
internal sealed class DelegatingJournalStore : IAddonOperationJournalStore
{
    private readonly IAddonOperationJournalStore _inner;
    private readonly Action<AddonOperationJournal, string>? _onSetPhase;

    public DelegatingJournalStore(
        IAddonOperationJournalStore inner,
        Action<AddonOperationJournal, string>? onSetPhase = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onSetPhase = onSetPhase;
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
        return _inner.Begin(
            addon, operationType, paths,
            previousState, previousInstallationMethod,
            previousVersion, previousDisplayedVersion,
            previousHadLocalAddon, targetInstallationMethod);
    }

    public void SetPhase(AddonOperationJournal journal, string phase)
    {
        _onSetPhase?.Invoke(journal, phase);
        _inner.SetPhase(journal, phase);
    }

    public void SetPhaseWithError(
        AddonOperationJournal journal,
        string phase,
        string errorMessage)
    {
        _inner.SetPhaseWithError(journal, phase, errorMessage);
    }

    public void Complete(AddonOperationJournal journal, string phase = AddonOperationPhase.Committed)
    {
        _inner.Complete(journal, phase);
    }

    public void CompleteWithAttention(
        AddonOperationJournal journal,
        string phase,
        string recoveryMessage)
    {
        _inner.CompleteWithAttention(journal, phase, recoveryMessage);
    }

    public void FailRolledBack(AddonOperationJournal journal, string errorMessage)
    {
        _inner.FailRolledBack(journal, errorMessage);
    }

    public void CancelRolledBack(AddonOperationJournal journal, string recoveryMessage)
    {
        _inner.CancelRolledBack(journal, recoveryMessage);
    }

    public void MarkRequiresAttention(
        AddonOperationJournal journal,
        string recoveryMessage,
        string? errorMessage = null)
    {
        _inner.MarkRequiresAttention(journal, recoveryMessage, errorMessage);
    }

    public void MarkRecoveryFailed(
        AddonOperationJournal journal,
        string errorMessage,
        string recoveryMessage)
    {
        _inner.MarkRecoveryFailed(journal, errorMessage, recoveryMessage);
    }

    public List<AddonOperationJournal> GetIncompleteOperations()
    {
        return _inner.GetIncompleteOperations();
    }

    public List<AddonOperationJournal> GetAttentionOperations()
    {
        return _inner.GetAttentionOperations();
    }
}
