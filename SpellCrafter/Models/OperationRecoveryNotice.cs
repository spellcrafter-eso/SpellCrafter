using SpellCrafter.Enums;

namespace SpellCrafter.Models;

public sealed class OperationRecoveryNotice
{
    public MessageDialogType Type { get; init; } = MessageDialogType.Info;
    public string Message { get; init; } = string.Empty;
    public string? Details { get; init; }
    public string? BackupPath { get; init; }
}