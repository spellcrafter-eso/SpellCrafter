using System;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

public interface IAddonOperationSubmissionService
{
    Task<QueuedOperationSubmissionResult> SubmitInstallAsync(
        Addon addon,
        AddonInstallationMethod installationMethod,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    Task<QueuedOperationSubmissionResult> SubmitUpdateAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    Task<QueuedOperationSubmissionResult> SubmitReinstallAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    Task<QueuedOperationSubmissionResult> SubmitDeleteAsync(
        Addon addon,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    Task<QueuedOperationSubmissionResult> SubmitDeleteAsync(
        Addon addon,
        DeleteOperationOptions options,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);
}
