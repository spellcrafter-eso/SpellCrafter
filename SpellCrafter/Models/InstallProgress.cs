namespace SpellCrafter.Models;

public enum InstallProgressStage
{
    Preparing,
    Downloading,
    Extracting,
    Moving,
    Committing,
    RollingBack,
    Completed,
    Warning
}

public sealed record InstallProgress(
    string AddonName,
    string Operation,
    InstallProgressStage Stage,
    string Message);
