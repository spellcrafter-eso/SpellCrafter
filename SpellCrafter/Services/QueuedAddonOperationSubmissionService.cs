using System;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

public sealed class QueuedAddonOperationSubmissionService : IAddonOperationSubmissionService
{
    private readonly Func<CancellationToken, Task<IAddonOperationSubmissionService>> _submissionFactory;

    public QueuedAddonOperationSubmissionService(IAddonOperationSubmissionService submissionService)
        : this(() => submissionService)
    {
        ArgumentNullException.ThrowIfNull(submissionService);
    }

    internal QueuedAddonOperationSubmissionService(Func<IAddonOperationSubmissionService> submissionFactory)
    {
        ArgumentNullException.ThrowIfNull(submissionFactory);

        _submissionFactory = _ => Task.FromResult(submissionFactory());
    }

    internal QueuedAddonOperationSubmissionService(
        Func<CancellationToken, Task<IAddonOperationSubmissionService>> submissionFactory)
    {
        _submissionFactory = submissionFactory ?? throw new ArgumentNullException(nameof(submissionFactory));
    }

    public Task<QueuedOperationSubmissionResult> SubmitInstallAsync(
        Addon addon,
        AddonInstallationMethod installationMethod,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return SubmitAsync(
            cancellationToken,
            service => service.SubmitInstallAsync(addon, installationMethod, recursive, progress, cancellationToken));
    }

    public Task<QueuedOperationSubmissionResult> SubmitUpdateAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return SubmitAsync(
            cancellationToken,
            service => service.SubmitUpdateAsync(addon, recursive, progress, cancellationToken));
    }

    public Task<QueuedOperationSubmissionResult> SubmitReinstallAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return SubmitAsync(
            cancellationToken,
            service => service.SubmitReinstallAsync(addon, recursive, progress, cancellationToken));
    }

    public Task<QueuedOperationSubmissionResult> SubmitDeleteAsync(
        Addon addon,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return SubmitAsync(
            cancellationToken,
            service => service.SubmitDeleteAsync(addon, progress, cancellationToken));
    }

    public Task<QueuedOperationSubmissionResult> SubmitDeleteAsync(
        Addon addon,
        DeleteOperationOptions options,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return SubmitAsync(
            cancellationToken,
            service => service.SubmitDeleteAsync(addon, options, progress, cancellationToken));
    }

    private async Task<QueuedOperationSubmissionResult> SubmitAsync(
        CancellationToken cancellationToken,
        Func<IAddonOperationSubmissionService, Task<QueuedOperationSubmissionResult>> submit)
    {
        var submissionService = await _submissionFactory(cancellationToken).ConfigureAwait(false);
        return await submit(submissionService).ConfigureAwait(false);
    }
}
