using SpellCrafter.Models;

namespace SpellCrafter.Tests.UnitTests;

public sealed class AddonOperationPhaseTests
{
    [Fact]
    public void AllPhases_AreUnique()
    {
        var phases = new HashSet<string>
        {
            AddonOperationPhase.Created,
            AddonOperationPhase.MarkingLocalAddonInstalling,
            AddonOperationPhase.ExtractingToStaging,
            AddonOperationPhase.StagingReady,
            AddonOperationPhase.BeforeExistingTargetBackup,
            AddonOperationPhase.AfterExistingTargetBackup,
            AddonOperationPhase.BeforeStagedTargetMove,
            AddonOperationPhase.AfterStagedTargetMove,
            AddonOperationPhase.BeforeTargetBackupForDelete,
            AddonOperationPhase.AfterTargetBackupForDelete,
            AddonOperationPhase.BeforeDatabaseCommit,
            AddonOperationPhase.Committed,
            AddonOperationPhase.FailedRolledBack,
            AddonOperationPhase.CanceledRolledBack,
            AddonOperationPhase.RecoveryInProgress,
            AddonOperationPhase.RecoveryCompleted,
            AddonOperationPhase.RecoveryFailed
        };

        // All const strings are distinct
        Assert.Equal(17, phases.Count);
    }

    [Fact]
    public void Created_HasExpectedValue()
    {
        Assert.Equal("Created", AddonOperationPhase.Created);
    }

    [Fact]
    public void Committed_HasExpectedValue()
    {
        Assert.Equal("Committed", AddonOperationPhase.Committed);
    }

    [Fact]
    public void FailedRolledBack_HasExpectedValue()
    {
        Assert.Equal("FailedRolledBack", AddonOperationPhase.FailedRolledBack);
    }

    [Fact]
    public void CanceledRolledBack_HasExpectedValue()
    {
        Assert.Equal("CanceledRolledBack", AddonOperationPhase.CanceledRolledBack);
    }

    [Fact]
    public void RecoveryInProgress_HasExpectedValue()
    {
        Assert.Equal("RecoveryInProgress", AddonOperationPhase.RecoveryInProgress);
    }

    [Fact]
    public void RecoveryCompleted_HasExpectedValue()
    {
        Assert.Equal("RecoveryCompleted", AddonOperationPhase.RecoveryCompleted);
    }

    [Fact]
    public void RecoveryFailed_HasExpectedValue()
    {
        Assert.Equal("RecoveryFailed", AddonOperationPhase.RecoveryFailed);
    }

    [Fact]
    public void AllInstallPhases_AreCorrect()
    {
        // Installation flow order
        Assert.Equal("Created", AddonOperationPhase.Created);
        Assert.Equal("MarkingLocalAddonInstalling", AddonOperationPhase.MarkingLocalAddonInstalling);
        Assert.Equal("ExtractingToStaging", AddonOperationPhase.ExtractingToStaging);
        Assert.Equal("StagingReady", AddonOperationPhase.StagingReady);
        Assert.Equal("BeforeStagedTargetMove", AddonOperationPhase.BeforeStagedTargetMove);
        Assert.Equal("AfterStagedTargetMove", AddonOperationPhase.AfterStagedTargetMove);
        Assert.Equal("BeforeDatabaseCommit", AddonOperationPhase.BeforeDatabaseCommit);
        Assert.Equal("Committed", AddonOperationPhase.Committed);
    }

    [Fact]
    public void AllUpdatePhases_AreCorrect()
    {
        Assert.Equal("BeforeExistingTargetBackup", AddonOperationPhase.BeforeExistingTargetBackup);
        Assert.Equal("AfterExistingTargetBackup", AddonOperationPhase.AfterExistingTargetBackup);
    }

    [Fact]
    public void AllDeletePhases_AreCorrect()
    {
        Assert.Equal("BeforeTargetBackupForDelete", AddonOperationPhase.BeforeTargetBackupForDelete);
        Assert.Equal("AfterTargetBackupForDelete", AddonOperationPhase.AfterTargetBackupForDelete);
    }
}