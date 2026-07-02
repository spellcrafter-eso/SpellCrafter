using SpellCrafter.Enums;
using SpellCrafter.Models;
using SpellCrafter.Services;

namespace SpellCrafter.Tests.UnitTests;

public sealed class QueuedAddonInstallationServiceTests
{
    private readonly IAddonOperationCoordinator _coordinator;
    private readonly QueuedAddonInstallationService _service;
    private readonly Addon _addon;

    public QueuedAddonInstallationServiceTests()
    {
        _coordinator = Substitute.For<IAddonOperationCoordinator>();
        _service = new QueuedAddonInstallationService(_coordinator);
        _addon = new Addon
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            UniqueId = 456,
            State = AddonState.NotInstalled
        };
    }

    [Fact]
    public void Constructor_WithCoordinatorFactory_DoesNotInvokeFactory()
    {
        var factoryCalls = 0;

        _ = new QueuedAddonInstallationService(() =>
        {
            factoryCalls++;
            return _coordinator;
        });

        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task InstallAsync_DelegatesToCoordinator()
    {
        var expectedResult = InstallResult.Success();
        _coordinator.EnqueueInstallAsync(_addon, AddonInstallationMethod.SpellCrafter, false, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResult));

        var result = await _service.InstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.Same(expectedResult, result);
        await _coordinator.Received(1).EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_DelegatesToCoordinator()
    {
        var expectedResult = InstallResult.Success();
        _coordinator.EnqueueUpdateAsync(_addon, true, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResult));

        var result = await _service.UpdateAsync(
            _addon, true, null, default);

        Assert.Same(expectedResult, result);
        await _coordinator.Received(1).EnqueueUpdateAsync(
            _addon, true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReinstallAsync_DelegatesToCoordinator()
    {
        var expectedResult = InstallResult.Success("warning");
        _coordinator.EnqueueReinstallAsync(_addon, true, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResult));

        var result = await _service.ReinstallAsync(
            _addon, true, null, default);

        Assert.Same(expectedResult, result);
        await _coordinator.Received(1).EnqueueReinstallAsync(
            _addon, true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToCoordinator()
    {
        var expectedResult = InstallResult.Success();
        _coordinator.EnqueueDeleteAsync(_addon, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResult));

        var result = await _service.DeleteAsync(
            _addon, null, default);

        Assert.Same(expectedResult, result);
        await _coordinator.Received(1).EnqueueDeleteAsync(
            _addon, null, Arg.Any<CancellationToken>());
    }
}
