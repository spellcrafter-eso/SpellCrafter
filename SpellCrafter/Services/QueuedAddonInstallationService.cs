using System;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

/// <summary>
/// An <see cref="IAddonInstallationService"/> implementation that enqueues
/// operations through <see cref="IAddonOperationCoordinator"/> instead of
/// executing them directly. Operations are processed with the coordinator's
/// concurrency policy (initially concurrency = 1).
/// </summary>
public sealed class QueuedAddonInstallationService : IAddonInstallationService
{
    private readonly Func<IAddonOperationCoordinator> _coordinatorFactory;

    public QueuedAddonInstallationService(IAddonOperationCoordinator coordinator)
        : this(() => coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
    }

    internal QueuedAddonInstallationService(Func<IAddonOperationCoordinator> coordinatorFactory)
    {
        _coordinatorFactory = coordinatorFactory ?? throw new ArgumentNullException(nameof(coordinatorFactory));
    }

    private IAddonOperationCoordinator Coordinator => _coordinatorFactory();

    public Task<InstallResult> InstallAsync(
        Addon addon,
        AddonInstallationMethod installationMethod,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Coordinator.EnqueueInstallAsync(addon, installationMethod, recursive, progress, cancellationToken);
    }

    public Task<InstallResult> UpdateAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Coordinator.EnqueueUpdateAsync(addon, recursive, progress, cancellationToken);
    }

    public Task<InstallResult> ReinstallAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Coordinator.EnqueueReinstallAsync(addon, recursive, progress, cancellationToken);
    }

    public Task<InstallResult> DeleteAsync(
        Addon addon,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Coordinator.EnqueueDeleteAsync(addon, progress, cancellationToken);
    }
}
