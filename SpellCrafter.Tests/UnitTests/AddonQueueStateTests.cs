using System;
using SpellCrafter.Enums;
using SpellCrafter.Models;
using SpellCrafter.Services;

namespace SpellCrafter.Tests.UnitTests;

public sealed class AddonQueueStateTests
{
    private static IAddonOperationSubmissionService CreateSubmissionService()
    {
        var service = Substitute.For<IAddonOperationSubmissionService>();
        service.SubmitInstallAsync(
                Arg.Any<Addon>(),
                Arg.Any<AddonInstallationMethod>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<InstallProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var addon = callInfo.Arg<Addon>();
                return Task.FromResult(QueuedOperationSubmissionResult.AcceptedOperation(
                    new QueuedOperation
                    {
                        OperationId = Guid.NewGuid(),
                        AddonCommonId = addon.CommonAddonId,
                        AddonName = addon.Name,
                        OperationType = AddonOperationType.Install,
                        Status = QueueOperationStatus.Pending,
                        RequestTime = DateTime.UtcNow
                    }));
            });
        service.SubmitUpdateAsync(
                Arg.Any<Addon>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<InstallProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var addon = callInfo.Arg<Addon>();
                return Task.FromResult(QueuedOperationSubmissionResult.AcceptedOperation(
                    new QueuedOperation
                    {
                        OperationId = Guid.NewGuid(),
                        AddonCommonId = addon.CommonAddonId,
                        AddonName = addon.Name,
                        OperationType = AddonOperationType.Update,
                        Status = QueueOperationStatus.Pending,
                        RequestTime = DateTime.UtcNow
                    }));
            });
        service.SubmitReinstallAsync(
                Arg.Any<Addon>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<InstallProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var addon = callInfo.Arg<Addon>();
                return Task.FromResult(QueuedOperationSubmissionResult.AcceptedOperation(
                    new QueuedOperation
                    {
                        OperationId = Guid.NewGuid(),
                        AddonCommonId = addon.CommonAddonId,
                        AddonName = addon.Name,
                        OperationType = AddonOperationType.Reinstall,
                        Status = QueueOperationStatus.Pending,
                        RequestTime = DateTime.UtcNow
                    }));
            });
        service.SubmitDeleteAsync(
                Arg.Any<Addon>(),
                Arg.Any<IProgress<InstallProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var addon = callInfo.Arg<Addon>();
                return Task.FromResult(QueuedOperationSubmissionResult.AcceptedOperation(
                    new QueuedOperation
                    {
                        OperationId = Guid.NewGuid(),
                        AddonCommonId = addon.CommonAddonId,
                        AddonName = addon.Name,
                        OperationType = AddonOperationType.Delete,
                        Status = QueueOperationStatus.Pending,
                        RequestTime = DateTime.UtcNow
                    }));
            });
        return service;
    }
    [Fact]
    public void PendingQueuedOperation_DisablesInstallAndEnablesCancel()
    {
        var addon = new Addon
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.NotInstalled
        };

        Assert.True(addon.InstallCommand.CanExecute(null));

        addon.SetQueuedOperation(new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = addon.CommonAddonId,
            AddonName = addon.Name,
            OperationType = AddonOperationType.Install,
            Status = QueueOperationStatus.Pending,
            RequestTime = DateTime.UtcNow
        });

        Assert.True(addon.HasQueuedOperation);
        Assert.True(addon.CanCancelQueuedOperation);
        Assert.Equal("Pending install", addon.QueuedOperationDisplayText);
        Assert.False(addon.InstallCommand.CanExecute(null));
        Assert.True(addon.CancelQueuedOperationCommand.CanExecute(null));
    }

    [Fact]
    public void InProgressQueuedOperation_DisablesInstallAndEnablesCancel()
    {
        var addon = new Addon
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.NotInstalled
        };

        addon.SetQueuedOperation(new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = addon.CommonAddonId,
            AddonName = addon.Name,
            OperationType = AddonOperationType.Install,
            Status = QueueOperationStatus.InProgress,
            RequestTime = DateTime.UtcNow
        });

        Assert.True(addon.HasQueuedOperation);
        Assert.True(addon.CanCancelQueuedOperation);
        Assert.Equal("Installing", addon.QueuedOperationDisplayText);
        Assert.False(addon.InstallCommand.CanExecute(null));
        Assert.True(addon.CancelQueuedOperationCommand.CanExecute(null));

        addon.ClearQueuedOperation();

        Assert.False(addon.HasQueuedOperation);
        Assert.True(addon.InstallCommand.CanExecute(null));
    }

    [Fact]
    public void InProgressQueuedOperation_WithCancelRequest_ShowsCancelRequested()
    {
        var addon = new Addon
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.NotInstalled
        };

        addon.SetQueuedOperation(new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = addon.CommonAddonId,
            AddonName = addon.Name,
            OperationType = AddonOperationType.Install,
            Status = QueueOperationStatus.InProgress,
            RequestTime = DateTime.UtcNow,
            CancelRequested = true
        });

        Assert.True(addon.HasQueuedOperation);
        Assert.True(addon.QueuedOperationCancelRequested);
        Assert.False(addon.CanCancelQueuedOperation);
        Assert.Equal("Cancel requested for install", addon.QueuedOperationDisplayText);
        Assert.False(addon.CancelQueuedOperationCommand.CanExecute(null));
    }

    [Fact]
    public async Task QueueInstall_SetsPendingQueuedOperationState()
    {
        var installationService = Substitute.For<IAddonInstallationService>();
        var submissionService = Substitute.For<IAddonOperationSubmissionService>();
        var addon = new Addon(installationService, submissionService)
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.NotInstalled
        };
        var operationId = Guid.NewGuid();

        submissionService.SubmitInstallAsync(
                addon,
                AddonInstallationMethod.SpellCrafter,
                true,
                null,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new QueuedOperationSubmissionResult(
                true,
                new QueuedOperationReceipt(
                    operationId,
                    addon.CommonAddonId,
                    addon.Name,
                    AddonOperationType.Install,
                    QueueOperationStatus.Pending,
                    false),
                null)));

        var result = await addon.QueueInstall();

        Assert.True(result.Accepted);
        Assert.Equal(operationId, addon.QueuedOperationId);
        Assert.True(addon.HasQueuedOperation);
        Assert.Equal("Pending install", addon.QueuedOperationDisplayText);
        Assert.False(addon.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task QueueUpdate_WhenAccepted_SetsPendingQueuedOperationState()
    {
        var installationService = Substitute.For<IAddonInstallationService>();
        var submissionService = CreateSubmissionService();
        var addon = new Addon(installationService, submissionService)
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.Outdated,
            Version = "1.0",
            LatestVersion = "2.0"
        };

        var result = await addon.QueueUpdate(false);

        Assert.True(result.Accepted);
        Assert.NotNull(addon.QueuedOperationId);
        Assert.Equal(AddonOperationType.Update, addon.QueuedOperationType);
        Assert.Equal(QueueOperationStatus.Pending, addon.QueuedOperationStatus);
        Assert.True(addon.HasQueuedOperation);
        Assert.False(addon.UpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task QueueUpdate_WhenAlreadyLatest_DoesNotSubmitAndReturnsRejected()
    {
        var installationService = Substitute.For<IAddonInstallationService>();
        var submissionService = Substitute.For<IAddonOperationSubmissionService>();
        var addon = new Addon(installationService, submissionService)
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.LatestVersion,
            Version = "2.0",
            LatestVersion = "2.0"
        };

        var result = await addon.QueueUpdate(false);

        Assert.False(result.Accepted);
        Assert.Equal("Addon is already up to date.", result.RejectionReason);
        Assert.Null(addon.QueuedOperationId);

        await submissionService.DidNotReceiveWithAnyArgs().SubmitUpdateAsync(
            default!, default, default, default);
    }

    [Fact]
    public async Task QueueReinstall_WhenAccepted_SetsQueuedOperationState()
    {
        var installationService = Substitute.For<IAddonInstallationService>();
        var submissionService = CreateSubmissionService();
        var addon = new Addon(installationService, submissionService)
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.LatestVersion
        };

        var result = await addon.QueueReinstall(false);

        Assert.True(result.Accepted);
        Assert.NotNull(addon.QueuedOperationId);
        Assert.Equal(AddonOperationType.Reinstall, addon.QueuedOperationType);
        Assert.Equal(QueueOperationStatus.Pending, addon.QueuedOperationStatus);
        Assert.True(addon.HasQueuedOperation);
        Assert.False(addon.ReinstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task QueueDelete_WhenAccepted_SetsQueuedOperationState()
    {
        var installationService = Substitute.For<IAddonInstallationService>();
        var submissionService = CreateSubmissionService();
        var addon = new Addon(installationService, submissionService)
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.LatestVersion
        };

        var result = await addon.QueueDelete(default);

        Assert.True(result.Accepted);
        Assert.NotNull(addon.QueuedOperationId);
        Assert.Equal(AddonOperationType.Delete, addon.QueuedOperationType);
        Assert.Equal(QueueOperationStatus.Pending, addon.QueuedOperationStatus);
        Assert.True(addon.HasQueuedOperation);
        Assert.False(addon.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task QueueInstall_WhenRejected_DoesNotChangeExistingQueuedState()
    {
        var installationService = Substitute.For<IAddonInstallationService>();
        var submissionService = Substitute.For<IAddonOperationSubmissionService>();
        var addon = new Addon(installationService, submissionService)
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.NotInstalled
        };

        submissionService.SubmitInstallAsync(
                addon,
                AddonInstallationMethod.SpellCrafter,
                true,
                null,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(QueuedOperationSubmissionResult.Rejected("Already queued.")));

        Assert.False(addon.HasQueuedOperation);

        var result = await addon.QueueInstall();

        Assert.False(result.Accepted);
        Assert.Equal("Already queued.", result.RejectionReason);
        Assert.False(addon.HasQueuedOperation);
        Assert.True(addon.InstallCommand.CanExecute(null));
    }
}
