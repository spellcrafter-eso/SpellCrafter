using SpellCrafter.Enums;

namespace SpellCrafter.Models;

/// <summary>
/// User intent for a single addon operation.
/// </summary>
public sealed record AddonOperationRequest(
    string OperationType,
    int RootCommonAddonId,
    string RootAddonName,
    bool Recursive,
    AddonInstallationMethod InstallationMethod,
    DeleteOperationOptions? DeleteOptions);