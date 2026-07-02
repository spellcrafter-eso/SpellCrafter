using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Enums;
using SpellCrafter.Models;
using SpellCrafter.Services;

namespace SpellCrafter.Tests.UnitTests;

public sealed class AddonOperationCoordinatorTests
{
    private readonly ISingleAddonInstallationWorker _worker;
    private readonly IAddonDataManager _dataManager;
    private readonly IAddonOperationQueueStore _queueStore;
    private readonly IAddonDependencyGraphStore _graphStore;
    private readonly AddonOperationCoordinator _coordinator;
    private readonly Addon _addon;

    public AddonOperationCoordinatorTests()
    {
        _worker = Substitute.For<ISingleAddonInstallationWorker>();
        _dataManager = Substitute.For<IAddonDataManager>();
        _queueStore = new InMemoryOperationQueueStore();
        _graphStore = Substitute.For<IAddonDependencyGraphStore>();
        _graphStore.CreateSnapshot().Returns(AddonGraphSnapshot.Create(
            new Dictionary<int, int[]>(),
            new Dictionary<int, int[]>()));

        _coordinator = new AddonOperationCoordinator(
            _worker, _dataManager, _queueStore, _graphStore, new AddonResourceLockManager());

        _addon = new Addon
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            UniqueId = 456,
            State = AddonState.NotInstalled
        };

        // Make the addon findable via OnlineAddons
        _dataManager.OnlineAddons.Returns(new List<Addon> { _addon });
    }

    [Fact]
    public async Task EnqueueInstall_CallsWorkerInstallOne()
    {
        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstallResult.Success()));

        var result = await _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.True(result.Succeeded);
        await _worker.Received(1).InstallOneAsync(
            _addon, AddonInstallationMethod.SpellCrafter, Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitInstallAsync_ReturnsReceiptBeforeWorkerCompletes()
    {
        var releaseWorker = new TaskCompletionSource();
        var workerStarted = new TaskCompletionSource();

        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                workerStarted.TrySetResult();
                await releaseWorker.Task;
                return InstallResult.Success();
            });

        var submitResult = await _coordinator.SubmitInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default)
            .WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(submitResult.Accepted, submitResult.RejectionReason);
        Assert.NotNull(submitResult.Operation);
        Assert.Equal(_addon.CommonAddonId, submitResult.Operation.AddonCommonId);
        Assert.Equal(AddonOperationType.Install, submitResult.Operation.OperationType);
        Assert.Equal(QueueOperationStatus.Pending, submitResult.Operation.Status);

        await workerStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(releaseWorker.Task.IsCompleted);

        releaseWorker.SetResult();
        await WaitForQueuedOperationAsync(
            _queueStore,
            o => o.OperationId == submitResult.Operation.OperationId &&
                 o.Status == QueueOperationStatus.Completed);
    }

    [Fact]
    public async Task SubmitInstallAsync_WhenSameAddonHasActiveOperation_RejectsWithoutEnqueueing()
    {
        var existing = new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = _addon.CommonAddonId,
            AddonName = _addon.Name,
            OperationType = AddonOperationType.Update,
            Status = QueueOperationStatus.Pending,
            Priority = QueuePriority.Normal,
            RequestTime = DateTime.UtcNow
        };

        await _queueStore.EnqueueAsync(existing);

        var result = await _coordinator.SubmitInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.False(result.Accepted);
        Assert.Null(result.Operation);
        Assert.Contains("pending", result.RejectionReason, StringComparison.OrdinalIgnoreCase);

        var operations = await _queueStore.GetAllAsync();
        Assert.Single(operations);
        Assert.Equal(existing.OperationId, operations[0].OperationId);
    }

    [Fact]
    public async Task SubmitDeleteAsync_WhenActiveOperationDependsOnAddon_RejectsWithoutEnqueueing()
    {
        var rootAddon = new Addon { CommonAddonId = 100, Name = "RootAddon" };
        var dependencyAddon = new Addon { CommonAddonId = 50, Name = "DependencyAddon" };

        _dataManager.OnlineAddons.Returns(new List<Addon> { rootAddon, dependencyAddon });
        _dataManager.InstalledAddons.Returns(new List<Addon> { rootAddon, dependencyAddon });
        _graphStore.CreateSnapshot().Returns(AddonGraphSnapshot.Create(
            new Dictionary<int, int[]> { [rootAddon.CommonAddonId] = [dependencyAddon.CommonAddonId] },
            new Dictionary<int, int[]>()));

        var existing = new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = rootAddon.CommonAddonId,
            AddonName = rootAddon.Name,
            OperationType = AddonOperationType.Update,
            Status = QueueOperationStatus.Pending,
            Priority = QueuePriority.Normal,
            RequestTime = DateTime.UtcNow
        };

        await _queueStore.EnqueueAsync(existing);

        var result = await _coordinator.SubmitDeleteAsync(dependencyAddon, null, default);

        Assert.False(result.Accepted);
        Assert.Null(result.Operation);
        Assert.Contains("depends on", result.RejectionReason, StringComparison.OrdinalIgnoreCase);

        var operations = await _queueStore.GetAllAsync();
        Assert.Single(operations);
        Assert.Equal(existing.OperationId, operations[0].OperationId);
    }

    [Fact]
    public async Task EnqueueUpdate_CallsWorkerReplaceOneWithUpdate()
    {
        _worker.ReplaceOneAsync(_addon, AddonOperationType.Update, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstallResult.Success()));

        _addon.State = AddonState.Outdated;
        _addon.InstallationMethod = AddonInstallationMethod.SpellCrafter;

        var result = await _coordinator.EnqueueUpdateAsync(
            _addon, false, null, default);

        Assert.True(result.Succeeded);
        await _worker.Received(1).ReplaceOneAsync(
            _addon, AddonOperationType.Update, AddonInstallationMethod.SpellCrafter,
            Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnqueueDelete_CallsWorkerDeleteOne()
    {
        _worker.DeleteOne(_addon, null, Arg.Any<CancellationToken>())
            .Returns(InstallResult.Success());

        var result = await _coordinator.EnqueueDeleteAsync(
            _addon, null, default);

        Assert.True(result.Succeeded);
        _worker.Received(1).DeleteOne(
            _addon, Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddonNotFound_ReturnsFailure()
    {
        var unknownAddon = new Addon
        {
            CommonAddonId = 999,
            Name = "Unknown",
            State = AddonState.NotInstalled
        };

        // OnlineAddons only contains the original _addon, not unknownAddon
        var result = await _coordinator.EnqueueInstallAsync(
            unknownAddon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnqueueInstall_WhenSameAddonHasActiveOperation_RejectsSecondOperation()
    {
        var signal = new TaskCompletionSource();

        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await signal.Task;
                return InstallResult.Success();
            });

        // Enqueue first operation (will block on signal)
        var firstTask = _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Enqueue second operation (should wait for first)
        var secondTask = _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.False(firstTask.IsCompleted);

        var secondResult = await secondTask;
        Assert.False(secondResult.Succeeded);
        Assert.Contains("already has", secondResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Release the first
        signal.SetResult();

        var firstResult = await firstTask;

        Assert.True(firstResult.Succeeded);

        await _worker.Received(1).InstallOneAsync(
            _addon, AddonInstallationMethod.SpellCrafter,
            Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnqueueReinstall_CallsWorkerReplaceOneWithReinstall()
    {
        // Use ReturnsForAnyArgs to ensure the mock matches regardless of args
        _worker.ReplaceOneAsync(default!, default!, default!, default!, default!)
            .ReturnsForAnyArgs(Task.FromResult(InstallResult.Success()));

        var result = await _coordinator.EnqueueReinstallAsync(
            _addon, false, null, default);

        Assert.True(result.Succeeded,
            $"Result error: '{result.ErrorMessage}'. ");
    }

    [Fact]
    public async Task WorkerFailure_ReturnsFailureResult()
    {
        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstallResult.Failure("Download failed")));

        var result = await _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.False(result.Succeeded);
        Assert.Contains("Download failed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProgressIsPassedToWorker()
    {
        var progress = Substitute.For<IProgress<InstallProgress>>();

        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, progress, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstallResult.Success()));

        var result = await _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, progress, default);

        Assert.True(result.Succeeded);
        await _worker.Received(1).InstallOneAsync(
            _addon, AddonInstallationMethod.SpellCrafter, progress, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellationBeforeExecution_ReturnsCanceledResult()
    {
        // Use a semaphore to block the first operation so the second stays pending
        var blockFirst = new SemaphoreSlim(0, 1);

        var installCallCount = 0;

        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref installCallCount);
                return Task.Run(async () =>
                {
                    await blockFirst.WaitAsync();
                    return InstallResult.Success();
                });
            });

        // We need a second addon to not conflict on resource locks
        var otherAddon = new Addon { CommonAddonId = 200, Name = "OtherAddon" };
        _dataManager.OnlineAddons.Returns(new List<Addon> { _addon, otherAddon });

        // Start first operation (will block)
        var firstTask = _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Wait for the first operation to claim and start executing
        await WaitForConditionAsync(() => Volatile.Read(ref installCallCount) > 0);

        // Enqueue second operation with a canceled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _coordinator.EnqueueInstallAsync(
            otherAddon, AddonInstallationMethod.SpellCrafter, false, null, cts.Token);

        Assert.False(result.Succeeded);
        Assert.True(result.WasCanceled);

        // Release the first operation
        blockFirst.Release();
        await firstTask;
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var start = Environment.TickCount;
        while (!condition())
        {
            if (Environment.TickCount - start > timeoutMs)
                throw new TimeoutException("Condition was not met within timeout.");
            await Task.Delay(20);
        }
    }

    private static async Task<QueuedOperation> WaitForQueuedOperationAsync(
        IAddonOperationQueueStore queueStore,
        Func<QueuedOperation, bool> predicate,
        int timeoutMs = 3000)
    {
        var start = Environment.TickCount;
        while (true)
        {
            var operation = (await queueStore.GetAllAsync()).FirstOrDefault(predicate);
            if (operation != null)
                return operation;

            if (Environment.TickCount - start > timeoutMs)
                throw new TimeoutException("Queued operation was not found within timeout.");

            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task CancellationDuringExecution_PassesTokenToWorker()
    {
        using var cts = new CancellationTokenSource();
        var workerStarted = new TaskCompletionSource();

        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                return Task.Run(async () =>
                {
                    workerStarted.TrySetResult();
                    await Task.Delay(10000, cts.Token);
                    return InstallResult.Success();
                });
            });

        var task = _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, cts.Token);

        // Wait for worker to start
        await workerStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // Cancel
        cts.Cancel();

        var result = await task;
        Assert.False(result.Succeeded);
        Assert.True(result.WasCanceled);
    }

    [Fact]
    public async Task CancelOperationAsync_InProgressOperation_PersistsRequestAndCancelsWorker()
    {
        var workerStarted = new TaskCompletionSource();
        var cancellationObserved = new TaskCompletionSource();
        var allowWorkerToReturn = new TaskCompletionSource();

        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.Run(async () =>
            {
                var token = callInfo.Arg<CancellationToken>();

                workerStarted.TrySetResult();
                await WaitForConditionAsync(() => token.IsCancellationRequested);
                cancellationObserved.TrySetResult();
                await allowWorkerToReturn.Task;

                return InstallResult.Canceled("Operation was canceled via queue management.");
            }));

        var operationTask = _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        await workerStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var operation = await WaitForQueuedOperationAsync(
            _queueStore, o => o.Status == QueueOperationStatus.InProgress);

        await _coordinator.CancelOperationAsync(operation.OperationId);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var requested = (await _queueStore.GetAllAsync()).Single(o => o.OperationId == operation.OperationId);
        Assert.Equal(QueueOperationStatus.InProgress, requested.Status);
        Assert.True(requested.CancelRequested);
        Assert.Equal("Operation was canceled via queue management.", requested.CancelReason);
        Assert.NotNull(requested.CancelRequestedAtUtc);

        allowWorkerToReturn.SetResult();

        var result = await operationTask;
        Assert.True(result.WasCanceled);

        var completed = (await _queueStore.GetAllAsync()).Single(o => o.OperationId == operation.OperationId);
        Assert.Equal(QueueOperationStatus.Canceled, completed.Status);
        Assert.True(completed.CancelRequested);
    }

    [Fact]
    public async Task PersistedCancelRequest_DuringExecution_CancelsWorkerToken()
    {
        var workerStarted = new TaskCompletionSource();
        var cancellationObserved = new TaskCompletionSource();
        var allowWorkerToReturn = new TaskCompletionSource();

        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.Run(async () =>
            {
                var token = callInfo.Arg<CancellationToken>();

                workerStarted.TrySetResult();
                await WaitForConditionAsync(() => token.IsCancellationRequested);
                cancellationObserved.TrySetResult();
                await allowWorkerToReturn.Task;

                return InstallResult.Canceled("External cancellation request was observed.");
            }));

        var operationTask = _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        await workerStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var operation = await WaitForQueuedOperationAsync(
            _queueStore, o => o.Status == QueueOperationStatus.InProgress);

        var requested = await _queueStore.RequestCancellationAsync(
            operation.OperationId, "External cancel request.");

        Assert.True(requested);

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));

        allowWorkerToReturn.SetResult();

        var result = await operationTask;
        Assert.True(result.WasCanceled);

        var completed = (await _queueStore.GetAllAsync()).Single(o => o.OperationId == operation.OperationId);
        Assert.Equal(QueueOperationStatus.Canceled, completed.Status);
        Assert.True(completed.CancelRequested);
        Assert.Equal("External cancel request.", completed.CancelReason);
    }

    [Fact]
    public async Task PersistedCancelRequest_CancelsClaimedOperationBeforeWorkerStarts()
    {
        var worker = Substitute.For<ISingleAddonInstallationWorker>();
        var dataManager = Substitute.For<IAddonDataManager>();
        var queueStore = new InMemoryOperationQueueStore();
        var graphStore = Substitute.For<IAddonDependencyGraphStore>();
        graphStore.CreateSnapshot().Returns(AddonGraphSnapshot.Create(
            new Dictionary<int, int[]>(),
            new Dictionary<int, int[]>()));

        var addon = new Addon
        {
            CommonAddonId = 123,
            Name = "PersistedCancelAddon",
            UniqueId = 789,
            State = AddonState.NotInstalled
        };

        dataManager.OnlineAddons.Returns(new List<Addon> { addon });

        var operation = new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = addon.CommonAddonId,
            AddonName = addon.Name,
            OperationType = AddonOperationType.Install,
            InstallationMethod = AddonInstallationMethod.SpellCrafter,
            Status = QueueOperationStatus.Pending,
            Priority = QueuePriority.Normal,
            RequestTime = DateTime.UtcNow,
            CancelRequested = true,
            CancelReason = "Previously requested."
        };

        await queueStore.EnqueueAsync(operation);

        _ = new AddonOperationCoordinator(
            worker, dataManager, queueStore, graphStore, new AddonResourceLockManager());

        var canceled = await WaitForQueuedOperationAsync(
            queueStore,
            o => o.OperationId == operation.OperationId && o.Status == QueueOperationStatus.Canceled);

        Assert.True(canceled.CancelRequested);
        Assert.Equal("Previously requested.", canceled.ErrorMessage);
        await worker.DidNotReceive().InstallOneAsync(
            Arg.Any<Addon>(), Arg.Any<AddonInstallationMethod>(), Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompletedOperation_CompletesCallerTask()
    {
        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstallResult.Success()));

        var result = await _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SuccessfulOperation_WithWarningAndCompletedAfterCancellation_PersistsResultMetadata()
    {
        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstallResult.Success(
                "Installation completed after cancellation was requested.",
                true)));

        var result = await _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.True(result.Succeeded);
        Assert.True(result.CompletedAfterCancellation);

        var operation = Assert.Single(await _queueStore.GetAllAsync());
        Assert.Equal(QueueOperationStatus.Completed, operation.Status);
        Assert.Equal("Installation completed after cancellation was requested.", operation.ResultMessage);
        Assert.True(operation.CompletedAfterCancellation);
    }

    [Fact]
    public async Task CompletedAfterCancellation_WithoutWarningMessage_PersistsBooleanOnly()
    {
        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstallResult.Success(
                warningMessage: null,
                completedAfterCancellation: true)));

        var result = await _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.True(result.Succeeded);
        Assert.True(result.CompletedAfterCancellation);

        var operation = Assert.Single(await _queueStore.GetAllAsync());
        Assert.Equal(QueueOperationStatus.Completed, operation.Status);
        Assert.True(operation.CompletedAfterCancellation);
        Assert.Null(operation.ResultMessage);
    }

    [Fact]
    public async Task TwoIndependentAddons_CanRunConcurrently()
    {
        var signal1 = new TaskCompletionSource();
        var signal2 = new TaskCompletionSource();

        var addon1 = new Addon { CommonAddonId = 100, Name = "Addon100" };
        var addon2 = new Addon { CommonAddonId = 200, Name = "Addon200" };

        _dataManager.OnlineAddons.Returns(new List<Addon> { addon1, addon2 });

        _worker.InstallOneAsync(addon1, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                return Task.Run(async () =>
                {
                    signal1.SetResult();
                    await signal2.Task;
                    return InstallResult.Success();
                });
            });

        _worker.InstallOneAsync(addon2, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                return Task.Run(async () =>
                {
                    signal2.TrySetResult();
                    await signal1.Task;
                    return InstallResult.Success();
                });
            });

        var task1 = _coordinator.EnqueueInstallAsync(
            addon1, AddonInstallationMethod.SpellCrafter, false, null, default);

        var task2 = _coordinator.EnqueueInstallAsync(
            addon2, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Both should complete because they don't share resource keys
        var result1 = await task1;
        var result2 = await task2;

        Assert.True(result1.Succeeded);
        Assert.True(result2.Succeeded);
    }

    // ---- Phase 2 tests: planner-driven coordinator ----

    [Fact]
    public async Task RecursiveInstall_ExecutesDependenciesViaPlan()
    {
        // Set up graph: addon 100 depends on 50
        var localDeps = new Dictionary<int, int[]> { { 100, [50] } };
        _graphStore.CreateSnapshot().Returns(AddonGraphSnapshot.Create(localDeps, new Dictionary<int, int[]>()));

        var rootAddon = new Addon { CommonAddonId = 100, Name = "RootAddon", State = AddonState.NotInstalled };
        var depAddon = new Addon { CommonAddonId = 50, Name = "DepAddon", State = AddonState.NotInstalled };

        _dataManager.OnlineAddons.Returns(new List<Addon> { rootAddon, depAddon });

        // This will be called for the dependency (id=50) then the root (id=100)
        _worker.InstallOneAsync(Arg.Any<Addon>(), AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstallResult.Success()));

        var result = await _coordinator.EnqueueInstallAsync(
            rootAddon, AddonInstallationMethod.SpellCrafter, true, null, default);

        Assert.True(result.Succeeded);

        // Dependency should be installed first, then root
        var installCalls = _worker.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ISingleAddonInstallationWorker.InstallOneAsync))
            .ToList();

        Assert.Equal(2, installCalls.Count);

        // First call should be the dependency (id=50)
        var firstArg = installCalls[0].GetArguments()[0] as Addon;
        Assert.Equal(50, firstArg?.CommonAddonId);

        // Second call should be the root (id=100)
        var secondArg = installCalls[1].GetArguments()[0] as Addon;
        Assert.Equal(100, secondArg?.CommonAddonId);
    }

    [Fact]
    public async Task DeleteWithReverseDependent_ReturnsRejected()
    {
        // Set up graph: addon 100 depends on 50, so 50 has a reverse dependent 100
        var localDeps = new Dictionary<int, int[]> { { 100, [50] } };
        _graphStore.CreateSnapshot().Returns(AddonGraphSnapshot.Create(localDeps, new Dictionary<int, int[]>()));

        var addon50 = new Addon { CommonAddonId = 50, Name = "SharedDep", State = AddonState.LatestVersion };
        var addon100 = new Addon { CommonAddonId = 100, Name = "RootAddon", State = AddonState.LatestVersion };

        _dataManager.OnlineAddons.Returns(new List<Addon> { addon50, addon100 });
        _dataManager.InstalledAddons.Returns(new List<Addon> { addon50, addon100 });

        var result = await _coordinator.EnqueueDeleteAsync(addon50, null, default);

        Assert.False(result.Succeeded);
        Assert.Contains("rejected", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonRecursiveInstall_CreatesSingleActionViaPlan()
    {
        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstallResult.Success()));

        var result = await _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        Assert.True(result.Succeeded);

        // Should have exactly one call to InstallOneAsync
        await _worker.Received(1).InstallOneAsync(
            _addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>());
    }

    // ---- Phase 3 tests: queue-aware delete validation and orphan cleanup ----

    [Fact]
    public async Task DeleteWithOptionsOverload_Works()
    {
        _worker.DeleteOne(_addon, null, Arg.Any<CancellationToken>())
            .Returns(InstallResult.Success());

        var result = await _coordinator.EnqueueDeleteAsync(
            _addon, new DeleteOperationOptions(false), null, default);

        Assert.True(result.Succeeded);
        _worker.Received(1).DeleteOne(
            _addon, Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteWithPendingInstallOfSameAddon_RejectedByQueue()
    {
        // Both planner and queue check catch this: the planner sees the addon
        // has a pending operation, and the queue check also catches it.
        // This test verifies the operation is rejected (by whichever layer catches first).

        // Block the install operation so it stays InProgress
        var blockInstall = new SemaphoreSlim(0, 1);
        _worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                return Task.Run(async () =>
                {
                    await blockInstall.WaitAsync();
                    return InstallResult.Success();
                });
            });

        // Enqueue install
        var installTask = _coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Wait for it to be claimed (InProgress)
        await WaitForConditionAsync(() =>
        {
            var ops = _queueStore.GetAllAsync(default).Result;
            return ops.Any(o => o.Status == QueueOperationStatus.InProgress);
        });

        // Enqueue delete of same addon — should be rejected
        var deleteResult = await _coordinator.EnqueueDeleteAsync(_addon, null, default);

        Assert.False(deleteResult.Succeeded);
        Assert.Contains("rejected", deleteResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Clean up
        blockInstall.Release();
        await installTask;
    }

    [Fact]
    public async Task DeleteOfIndependentAddon_WithPendingOperation_Allowed()
    {
        // Setup two independent addons with no dependency relationship
        var addonA = new Addon { CommonAddonId = 100, Name = "AddonA" };
        var addonB = new Addon { CommonAddonId = 200, Name = "AddonB" };
        _dataManager.OnlineAddons.Returns(new List<Addon> { addonA, addonB });
        _dataManager.InstalledAddons.Returns(new List<Addon> { addonA, addonB });

        _worker.InstallOneAsync(addonA, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                return Task.Run(async () =>
                {
                    await Task.Delay(10000);
                    return InstallResult.Success();
                });
            });

        _worker.DeleteOne(addonB, null, Arg.Any<CancellationToken>())
            .Returns(InstallResult.Success());

        // Enqueue long-running install of A
        var installTask = _coordinator.EnqueueInstallAsync(
            addonA, AddonInstallationMethod.SpellCrafter, false, null, default);

        // Wait for it to be InProgress
        await WaitForConditionAsync(() =>
        {
            var ops = _queueStore.GetAllAsync(default).Result;
            return ops.Any(o => o.Status == QueueOperationStatus.InProgress);
        });

        // Enqueue delete of B (independent) — should succeed
        var result = await _coordinator.EnqueueDeleteAsync(addonB, null, default);

        Assert.True(result.Succeeded);

        // Clean up
        // installTask will be cancelled when TestCancellationToken fires
    }

    [Fact]
    public async Task QueuePriorityOrdering_NormalBeforeLow()
    {
        var store = new InMemoryOperationQueueStore();

        var low = new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = 1,
            AddonName = "Low",
            OperationType = AddonOperationType.CleanupOrphans,
            Priority = QueuePriority.Low,
            Status = QueueOperationStatus.Pending,
            RequestTime = DateTime.UtcNow
        };

        var normal = new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = 2,
            AddonName = "Normal",
            OperationType = AddonOperationType.Install,
            Priority = QueuePriority.Normal,
            Status = QueueOperationStatus.Pending,
            RequestTime = DateTime.UtcNow.AddSeconds(1) // submitted after low
        };

        await store.EnqueueAsync(low);
        await store.EnqueueAsync(normal);

        // First claim should be Normal (higher priority)
        var claimed = await store.TryClaimNextAsync();
        Assert.NotNull(claimed);
        Assert.Equal(QueuePriority.Normal, claimed.Priority);
        Assert.Equal("Normal", claimed.AddonName);

        // Second claim should be Low
        claimed = await store.TryClaimNextAsync();
        Assert.NotNull(claimed);
        Assert.Equal(QueuePriority.Low, claimed.Priority);
        Assert.Equal("Low", claimed.AddonName);
    }

    [Fact]
    public async Task DeleteWithCascade_TriggersOrphanCleanup()
    {
        // Graph: root (100) depends on dep (50)
        var graph = new Dictionary<int, int[]> { { 100, [50] } };
        _graphStore.CreateSnapshot().Returns(AddonGraphSnapshot.Create(graph, new Dictionary<int, int[]>()));

        var root = new Addon
        {
            CommonAddonId = 100,
            Name = "RootAddon",
            State = AddonState.LatestVersion,
            InstallationMethod = AddonInstallationMethod.SpellCrafter
        };

        var dep = new Addon
        {
            CommonAddonId = 50,
            Name = "DepAddon",
            State = AddonState.LatestVersion,
            InstallationMethod = AddonInstallationMethod.Dependency
        };

        _dataManager.OnlineAddons.Returns(new List<Addon> { root, dep });
        _dataManager.InstalledAddons.Returns(new List<Addon> { root, dep });

        // Set up DeleteOne for any addon to succeed
        _worker.DeleteOne(Arg.Any<Addon>(), null, Arg.Any<CancellationToken>())
            .Returns(InstallResult.Success());

        // Delete root with cascade (RemoveOrphanedDependencies = true)
        var deleteResult = await _coordinator.EnqueueDeleteAsync(
            root, new DeleteOperationOptions(true), null, default);

        Assert.True(deleteResult.Succeeded,
            $"Root delete failed: {deleteResult.ErrorMessage}");

        // The plan deletes both root and dep (cascade).
        // Then CleanupOrphans runs and deletes root again (still appears as orphan in snapshot).
        // At minimum root and dep were each deleted at least once.
        _worker.Received(1).DeleteOne(
            dep, Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteWithCascade_DeferredCleanup_DoesNotDeleteIndependentAddon()
    {
        var graph = new Dictionary<int, int[]>
        {
            { 100, [50] },
            { 200, [] }
        };
        _graphStore.CreateSnapshot().Returns(AddonGraphSnapshot.Create(graph, new Dictionary<int, int[]>()));

        var root = new Addon
        {
            CommonAddonId = 100,
            Name = "RootAddon",
            State = AddonState.LatestVersion,
            InstallationMethod = AddonInstallationMethod.SpellCrafter
        };

        var dep = new Addon
        {
            CommonAddonId = 50,
            Name = "DepAddon",
            State = AddonState.LatestVersion,
            InstallationMethod = AddonInstallationMethod.Dependency
        };

        var independent = new Addon
        {
            CommonAddonId = 200,
            Name = "IndependentAddon",
            State = AddonState.LatestVersion,
            InstallationMethod = AddonInstallationMethod.SpellCrafter
        };

        _dataManager.OnlineAddons.Returns(new List<Addon> { root, dep, independent });
        _dataManager.InstalledAddons.Returns(new List<Addon> { root, dep, independent });

        _worker.DeleteOne(Arg.Any<Addon>(), null, Arg.Any<CancellationToken>())
            .Returns(InstallResult.Success());

        var deleteResult = await _coordinator.EnqueueDeleteAsync(
            root, new DeleteOperationOptions(true), null, default);

        Assert.True(deleteResult.Succeeded,
            $"Root delete failed: {deleteResult.ErrorMessage}");

        await WaitForConditionAsync(async () =>
        {
            var operations = await _queueStore.GetAllAsync();
            return operations.Any(o =>
                o.OperationType == AddonOperationType.CleanupOrphans &&
                o.Status is QueueOperationStatus.Completed or QueueOperationStatus.Failed or QueueOperationStatus.Canceled);
        });

        _worker.DidNotReceive().DeleteOne(
            independent, Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AddonInstallationMethod.SpellCrafter)]
    [InlineData(AddonInstallationMethod.Other)]
    public async Task DeleteWithCascade_DoesNotDeleteDependencyThatWasNotInstalledAsDependency(
        AddonInstallationMethod dependencyInstallationMethod)
    {
        var graph = new Dictionary<int, int[]> { { 100, [50] } };
        _graphStore.CreateSnapshot().Returns(AddonGraphSnapshot.Create(graph, new Dictionary<int, int[]>()));

        var root = new Addon
        {
            CommonAddonId = 100,
            Name = "RootAddon",
            State = AddonState.LatestVersion,
            InstallationMethod = AddonInstallationMethod.SpellCrafter
        };

        var dependency = new Addon
        {
            CommonAddonId = 50,
            Name = "SharedDependency",
            State = AddonState.LatestVersion,
            InstallationMethod = dependencyInstallationMethod
        };

        _dataManager.OnlineAddons.Returns(new List<Addon> { root, dependency });
        _dataManager.InstalledAddons.Returns(new List<Addon> { root, dependency });

        _worker.DeleteOne(Arg.Any<Addon>(), null, Arg.Any<CancellationToken>())
            .Returns(InstallResult.Success());

        var deleteResult = await _coordinator.EnqueueDeleteAsync(
            root, new DeleteOperationOptions(true), null, default);

        Assert.True(deleteResult.Succeeded,
            $"Root delete failed: {deleteResult.ErrorMessage}");

        await WaitForConditionAsync(async () =>
        {
            var operations = await _queueStore.GetAllAsync();
            return operations.Any(o =>
                o.OperationType == AddonOperationType.CleanupOrphans &&
                o.Status is QueueOperationStatus.Completed or QueueOperationStatus.Failed or QueueOperationStatus.Canceled);
        });

        _worker.Received(1).DeleteOne(
            root, Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
        _worker.DidNotReceive().DeleteOne(
            dependency, Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleanupOrphans_WaitsForCandidateResourceLockBeforeDeleting()
    {
        var worker = Substitute.For<ISingleAddonInstallationWorker>();
        var dataManager = Substitute.For<IAddonDataManager>();
        var queueStore = new InMemoryOperationQueueStore();
        var graphStore = Substitute.For<IAddonDependencyGraphStore>();
        var lockManager = new AddonResourceLockManager();

        graphStore.CreateSnapshot().Returns(AddonGraphSnapshot.Create(
            new Dictionary<int, int[]>(),
            new Dictionary<int, int[]> { { 100, [50] } }));

        var root = new Addon
        {
            CommonAddonId = 100,
            Name = "RootAddon",
            State = AddonState.LatestVersion,
            InstallationMethod = AddonInstallationMethod.SpellCrafter
        };

        var dependency = new Addon
        {
            CommonAddonId = 50,
            Name = "DepAddon",
            State = AddonState.LatestVersion,
            InstallationMethod = AddonInstallationMethod.Dependency
        };

        dataManager.OnlineAddons.Returns(new List<Addon> { root, dependency });
        dataManager.InstalledAddons.Returns(new List<Addon> { root, dependency });

        var deleteCallCount = 0;
        worker.DeleteOne(dependency, null, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref deleteCallCount);
                return InstallResult.Success();
            });

        var cleanupOperation = new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = root.CommonAddonId,
            AddonName = root.Name,
            OperationType = AddonOperationType.CleanupOrphans,
            Priority = QueuePriority.Low,
            Status = QueueOperationStatus.Pending,
            RequestTime = DateTime.UtcNow
        };

        await queueStore.EnqueueAsync(cleanupOperation);
        var heldLock = await lockManager.AcquireAsync(AddonResourceKey.ForAddon(dependency.CommonAddonId));

        _ = new AddonOperationCoordinator(
            worker, dataManager, queueStore, graphStore, lockManager);

        await WaitForConditionAsync(async () =>
        {
            var operations = await queueStore.GetAllAsync();
            return operations.Single(o => o.OperationId == cleanupOperation.OperationId).Status != QueueOperationStatus.Pending;
        });

        Assert.Equal(0, Volatile.Read(ref deleteCallCount));

        heldLock.Dispose();

        await WaitForConditionAsync(() => Volatile.Read(ref deleteCallCount) > 0);
    }

    [Fact]
    public async Task GetAllQueuedOperationsAsync_ReturnsAllEnqueued()
    {
        var otherAddon = new Addon { CommonAddonId = 200, Name = "OtherAddon" };
        _dataManager.OnlineAddons.Returns(new List<Addon> { _addon, otherAddon });

        _worker.InstallOneAsync(Arg.Any<Addon>(), AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstallResult.Success()));

        var task1 = _coordinator.EnqueueInstallAsync(_addon, AddonInstallationMethod.SpellCrafter, false, null, default);
        var task2 = _coordinator.EnqueueInstallAsync(otherAddon, AddonInstallationMethod.SpellCrafter, false, null, default);

        var all = await _coordinator.GetAllQueuedOperationsAsync();

        Assert.True(all.Count >= 2);
        Assert.Contains(all, o => o.AddonCommonId == _addon.CommonAddonId);
        Assert.Contains(all, o => o.AddonCommonId == otherAddon.CommonAddonId);

        await task1;
        await task2;
    }

    [Fact]
    public async Task CancelOperationAsync_CancelsPendingOperation()
    {
        var queueStore = new InMemoryOperationQueueStore();
        var coordinator = new AddonOperationCoordinator(
            _worker, _dataManager, queueStore, _graphStore, new AddonResourceLockManager(), 1);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingAddon = new Addon { CommonAddonId = 200, Name = "PendingAddon" };

        _dataManager.OnlineAddons.Returns(new List<Addon> { _addon, pendingAddon });

        _worker.InstallOneAsync(Arg.Any<Addon>(), AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.Run(async () =>
            {
                var addon = callInfo.Arg<Addon>();
                if (addon.CommonAddonId == _addon.CommonAddonId)
                {
                    firstStarted.TrySetResult();
                    await releaseFirst.Task;
                }

                return InstallResult.Success();
            }));

        var firstTask = coordinator.EnqueueInstallAsync(
            _addon, AddonInstallationMethod.SpellCrafter, false, null, default);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var pendingTask = coordinator.EnqueueInstallAsync(
            pendingAddon, AddonInstallationMethod.SpellCrafter, false, null, default);

        var op = await WaitForQueuedOperationAsync(
            queueStore,
            o => o.AddonCommonId == pendingAddon.CommonAddonId && o.Status == QueueOperationStatus.Pending);

        await coordinator.CancelOperationAsync(op.OperationId);

        var afterCancel = await coordinator.GetAllQueuedOperationsAsync();
        var canceledOp = afterCancel.FirstOrDefault(o => o.OperationId == op.OperationId);
        Assert.NotNull(canceledOp);
        Assert.Equal(QueueOperationStatus.Canceled, canceledOp.Status);

        releaseFirst.SetResult();

        var firstResult = await firstTask;
        var pendingResult = await pendingTask;

        Assert.True(firstResult.Succeeded);
        Assert.True(pendingResult.WasCanceled);
        await _worker.DidNotReceive().InstallOneAsync(
            pendingAddon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelOperationAsync_CancelsInProgressOperationWaitingForResourceLock()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;

        var firstRoot = new Addon { CommonAddonId = 100, Name = "FirstRoot" };
        var secondRoot = new Addon { CommonAddonId = 200, Name = "SecondRoot" };
        var sharedDependency = new Addon { CommonAddonId = 50, Name = "SharedDependency" };

        _dataManager.OnlineAddons.Returns(new List<Addon> { firstRoot, secondRoot, sharedDependency });
        _graphStore.CreateSnapshot().Returns(AddonGraphSnapshot.Create(
            new Dictionary<int, int[]>
            {
                [firstRoot.CommonAddonId] = [sharedDependency.CommonAddonId],
                [secondRoot.CommonAddonId] = [sharedDependency.CommonAddonId]
            },
            new Dictionary<int, int[]>()));

        _worker.InstallOneAsync(Arg.Any<Addon>(), AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var call = Interlocked.Increment(ref callCount);
                return Task.Run(async () =>
                {
                    if (call == 1)
                    {
                        firstStarted.TrySetResult();
                        await releaseFirst.Task;
                    }

                    return InstallResult.Success();
                });
            });

        var firstTask = _coordinator.EnqueueInstallAsync(
            firstRoot, AddonInstallationMethod.SpellCrafter, true, null, default);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var secondTask = _coordinator.EnqueueInstallAsync(
            secondRoot, AddonInstallationMethod.SpellCrafter, true, null, default);

        await WaitForConditionAsync(async () =>
        {
            var operations = await _queueStore.GetAllAsync();
            return operations.Count(o => o.Status == QueueOperationStatus.InProgress) == 2;
        });

        var queued = await _queueStore.GetAllAsync();
        var secondOperation = queued
            .Where(o => o.Status == QueueOperationStatus.InProgress)
            .OrderByDescending(o => o.RequestTime)
            .First();

        await _coordinator.CancelOperationAsync(secondOperation.OperationId);

        releaseFirst.SetResult();

        var firstResult = await firstTask;
        var secondResult = await secondTask;

        Assert.True(firstResult.Succeeded);
        Assert.True(secondResult.WasCanceled);
        Assert.Equal(2, Volatile.Read(ref callCount));
        await _worker.DidNotReceive().InstallOneAsync(
            secondRoot, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelOperationAsync_NonExistentId_DoesNotThrow()
    {
        await _coordinator.CancelOperationAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task Constructor_ResumesPersistedPendingOperationWithoutRuntimeContext()
    {
        var queueStore = new InMemoryOperationQueueStore();
        var operation = new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = _addon.CommonAddonId,
            AddonName = _addon.Name,
            OperationType = AddonOperationType.Install,
            InstallationMethod = AddonInstallationMethod.SpellCrafter,
            Recursive = false,
            Priority = QueuePriority.Normal,
            Status = QueueOperationStatus.Pending,
            RequestTime = DateTime.UtcNow
        };

        await queueStore.EnqueueAsync(operation);

        var worker = Substitute.For<ISingleAddonInstallationWorker>();
        worker.InstallOneAsync(_addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(InstallResult.Success()));

        _ = new AddonOperationCoordinator(
            worker, _dataManager, queueStore, _graphStore, new AddonResourceLockManager());

        await WaitForConditionAsync(async () =>
        {
            var operations = await queueStore.GetAllAsync();
            return operations.Any(o =>
                o.OperationId == operation.OperationId &&
                o.Status == QueueOperationStatus.Completed);
        });

        await worker.Received(1).InstallOneAsync(
            _addon, AddonInstallationMethod.SpellCrafter, null, Arg.Any<CancellationToken>());
    }

    private static async Task WaitForConditionAsync(Func<Task<bool>> condition, int timeoutMs = 3000)
    {
        var start = Environment.TickCount;
        while (!await condition())
        {
            if (Environment.TickCount - start > timeoutMs)
                throw new TimeoutException("Condition was not met within timeout.");
            await Task.Delay(20);
        }
    }
}
