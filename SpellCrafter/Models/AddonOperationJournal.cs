using System;
using SpellCrafter.Enums;
using SQLite;

namespace SpellCrafter.Models;

public class AddonOperationJournal
{
    [PrimaryKey] [AutoIncrement] public int Id { get; set; }

    [MaxLength(32)] public string OperationId { get; set; } = string.Empty;

    [MaxLength(20)] public string OperationType { get; set; } = string.Empty;

    [MaxLength(30)] public string Phase { get; set; } = AddonOperationPhase.Created;

    public bool IsComplete { get; set; }

    public bool RequiresAttention { get; set; }

    public int CommonAddonId { get; set; }

    public int? UniqueId { get; set; }

    [MaxLength(150)] public string AddonName { get; set; } = string.Empty;

    public bool PreviousHadLocalAddon { get; set; }

    public AddonState PreviousState { get; set; }

    public AddonInstallationMethod PreviousInstallationMethod { get; set; }

    [MaxLength(20)] public string PreviousVersion { get; set; } = string.Empty;

    [MaxLength(20)] public string PreviousDisplayedVersion { get; set; } = string.Empty;

    public AddonInstallationMethod TargetInstallationMethod { get; set; }

    [MaxLength(20)] public string TargetVersion { get; set; } = string.Empty;

    [MaxLength(20)] public string TargetDisplayedVersion { get; set; } = string.Empty;

    public string AddonsRootDirectory { get; set; } = string.Empty;

    public string OperationRootDirectory { get; set; } = string.Empty;

    public string DownloadDirectory { get; set; } = string.Empty;

    public string StagingDirectory { get; set; } = string.Empty;

    public string BackupDirectory { get; set; } = string.Empty;

    public string TargetAddonDirectory { get; set; } = string.Empty;

    public string StagedAddonDirectory { get; set; } = string.Empty;

    public string BackupAddonDirectory { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string LastErrorMessage { get; set; } = string.Empty;

    public string RecoveryMessage { get; set; } = string.Empty;
}