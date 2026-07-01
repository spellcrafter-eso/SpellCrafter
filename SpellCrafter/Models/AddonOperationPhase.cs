namespace SpellCrafter.Models;

public static class AddonOperationPhase
{
    public const string Created = "Created";
    public const string MarkingLocalAddonInstalling = "MarkingLocalAddonInstalling";
    public const string ExtractingToStaging = "ExtractingToStaging";
    public const string StagingReady = "StagingReady";
    public const string BeforeExistingTargetBackup = "BeforeExistingTargetBackup";
    public const string AfterExistingTargetBackup = "AfterExistingTargetBackup";
    public const string BeforeStagedTargetMove = "BeforeStagedTargetMove";
    public const string AfterStagedTargetMove = "AfterStagedTargetMove";
    public const string BeforeTargetBackupForDelete = "BeforeTargetBackupForDelete";
    public const string AfterTargetBackupForDelete = "AfterTargetBackupForDelete";
    public const string BeforeDatabaseCommit = "BeforeDatabaseCommit";
    public const string Committed = "Committed";
    public const string FailedRolledBack = "FailedRolledBack";
    public const string CanceledRolledBack = "CanceledRolledBack";
    public const string RecoveryInProgress = "RecoveryInProgress";
    public const string RecoveryCompleted = "RecoveryCompleted";
    public const string RecoveryFailed = "RecoveryFailed";
}