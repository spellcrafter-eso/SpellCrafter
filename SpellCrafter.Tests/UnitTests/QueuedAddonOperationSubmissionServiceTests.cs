using SpellCrafter.Enums;
using SpellCrafter.Models;
using SpellCrafter.Services;

namespace SpellCrafter.Tests.UnitTests;

public sealed class QueuedAddonOperationSubmissionServiceTests
{
    [Fact]
    public void Constructor_WithSubmissionFactory_DoesNotInvokeFactory()
    {
        var factoryCalls = 0;

        _ = new QueuedAddonOperationSubmissionService(() =>
        {
            factoryCalls++;
            return Substitute.For<IAddonOperationSubmissionService>();
        });

        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public void Constructor_WithAsyncSubmissionFactory_DoesNotInvokeFactory()
    {
        var factoryCalls = 0;

        _ = new QueuedAddonOperationSubmissionService(_ =>
        {
            factoryCalls++;
            return Task.FromResult(Substitute.For<IAddonOperationSubmissionService>());
        });

        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task SubmitInstallAsync_DelegatesToSubmissionService()
    {
        var inner = Substitute.For<IAddonOperationSubmissionService>();
        var service = new QueuedAddonOperationSubmissionService(inner);
        var addon = new Addon
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.NotInstalled
        };
        var expected = QueuedOperationSubmissionResult.AcceptedOperation(new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = addon.CommonAddonId,
            AddonName = addon.Name,
            OperationType = AddonOperationType.Install,
            Status = QueueOperationStatus.Pending,
            RequestTime = DateTime.UtcNow
        });

        inner.SubmitInstallAsync(
                addon,
                AddonInstallationMethod.SpellCrafter,
                true,
                null,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));

        var result = await service.SubmitInstallAsync(
            addon, AddonInstallationMethod.SpellCrafter, true, null, default);

        Assert.Same(expected, result);
        await inner.Received(1).SubmitInstallAsync(
            addon,
            AddonInstallationMethod.SpellCrafter,
            true,
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitUpdateAsync_DelegatesToSubmissionService()
    {
        var inner = Substitute.For<IAddonOperationSubmissionService>();
        var service = new QueuedAddonOperationSubmissionService(inner);
        var addon = new Addon
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.Outdated
        };
        var expected = QueuedOperationSubmissionResult.AcceptedOperation(new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = addon.CommonAddonId,
            AddonName = addon.Name,
            OperationType = AddonOperationType.Update,
            Status = QueueOperationStatus.Pending,
            RequestTime = DateTime.UtcNow
        });

        inner.SubmitUpdateAsync(
                addon,
                true,
                null,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));

        var result = await service.SubmitUpdateAsync(addon, true, null, default);

        Assert.Same(expected, result);
        await inner.Received(1).SubmitUpdateAsync(
            addon, true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitReinstallAsync_DelegatesToSubmissionService()
    {
        var inner = Substitute.For<IAddonOperationSubmissionService>();
        var service = new QueuedAddonOperationSubmissionService(inner);
        var addon = new Addon
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.LatestVersion
        };
        var expected = QueuedOperationSubmissionResult.AcceptedOperation(new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = addon.CommonAddonId,
            AddonName = addon.Name,
            OperationType = AddonOperationType.Reinstall,
            Status = QueueOperationStatus.Pending,
            RequestTime = DateTime.UtcNow
        });

        inner.SubmitReinstallAsync(
                addon,
                true,
                null,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));

        var result = await service.SubmitReinstallAsync(addon, true, null, default);

        Assert.Same(expected, result);
        await inner.Received(1).SubmitReinstallAsync(
            addon, true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitDeleteAsync_WithoutOptions_DelegatesToSubmissionService()
    {
        var inner = Substitute.For<IAddonOperationSubmissionService>();
        var service = new QueuedAddonOperationSubmissionService(inner);
        var addon = new Addon
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.LatestVersion
        };
        var expected = QueuedOperationSubmissionResult.AcceptedOperation(new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = addon.CommonAddonId,
            AddonName = addon.Name,
            OperationType = AddonOperationType.Delete,
            Status = QueueOperationStatus.Pending,
            RequestTime = DateTime.UtcNow
        });

        inner.SubmitDeleteAsync(
                addon,
                Arg.Any<IProgress<InstallProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));

        var result = await service.SubmitDeleteAsync(addon, (IProgress<InstallProgress>?)null, default);

        Assert.Same(expected, result);
        await inner.Received(1).SubmitDeleteAsync(
            addon, Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitDeleteAsync_WithOptions_DelegatesToSubmissionService()
    {
        var inner = Substitute.For<IAddonOperationSubmissionService>();
        var service = new QueuedAddonOperationSubmissionService(inner);
        var addon = new Addon
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.LatestVersion
        };
        var options = new DeleteOperationOptions(true);
        var expected = QueuedOperationSubmissionResult.AcceptedOperation(new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = addon.CommonAddonId,
            AddonName = addon.Name,
            OperationType = AddonOperationType.Delete,
            Status = QueueOperationStatus.Pending,
            RequestTime = DateTime.UtcNow
        });

        inner.SubmitDeleteAsync(
                addon,
                options,
                Arg.Any<IProgress<InstallProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));

        var result = await service.SubmitDeleteAsync(addon, options, null, default);

        Assert.Same(expected, result);
        await inner.Received(1).SubmitDeleteAsync(
            addon, options, Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_UsesAsyncFactoryLazilyAndPassesCancellationToken()
    {
        var factoryCalls = 0;
        var capturedTokens = new List<CancellationToken>();

        var service = new QueuedAddonOperationSubmissionService(async ct =>
        {
            factoryCalls++;
            capturedTokens.Add(ct);
            var inner = Substitute.For<IAddonOperationSubmissionService>();
            inner.SubmitInstallAsync(
                    Arg.Any<Addon>(),
                    Arg.Any<AddonInstallationMethod>(),
                    Arg.Any<bool>(),
                    Arg.Any<IProgress<InstallProgress>>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(QueuedOperationSubmissionResult.AcceptedOperation(
                    new QueuedOperation
                    {
                        OperationId = Guid.NewGuid(),
                        AddonCommonId = 100,
                        AddonName = "Test",
                        OperationType = AddonOperationType.Install,
                        Status = QueueOperationStatus.Pending,
                        RequestTime = DateTime.UtcNow
                    })));
            return inner;
        });

        Assert.Equal(0, factoryCalls);

        using var testCts = new CancellationTokenSource();
        var addon = new Addon { CommonAddonId = 100, Name = "Test" };
        await service.SubmitInstallAsync(addon, AddonInstallationMethod.SpellCrafter, true, null, testCts.Token);

        Assert.Equal(1, factoryCalls);
        Assert.Contains(testCts.Token, capturedTokens);
    }
}
