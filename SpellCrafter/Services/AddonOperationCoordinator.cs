using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

/// <summary>
/// Coordinates queued addon operations. Processes pending operations with
/// configurable concurrency, using per-resource locks to serialize operations
/// that conflict (same addon, same folder).
/// </summary>
public sealed class AddonOperationCoordinator : IAddonOperationCoordinator, IAddonOperationSubmissionService
{
    private readonly ISingleAddonInstallationWorker _worker;
    private readonly IAddonDataManager _dataManager;
    private readonly IAddonOperationQueueStore _queueStore;
    private readonly IAddonDependencyGraphStore _graphStore;
    private readonly AddonResourceLockManager _lockManager;
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly Channel<byte> _signalChannel;
    private readonly ConcurrentDictionary<Guid, QueuedOperationExecutionContext> _executionContexts;
    private int _isRunning;

    private const int MaxReplanRetries = 3;
    private const string QueueCancellationMessage = "Operation was canceled via queue management.";
    private static readonly TimeSpan PersistedCancellationPollInterval = TimeSpan.FromMilliseconds(250);

    public AddonOperationCoordinator(
        ISingleAddonInstallationWorker worker,
        IAddonDataManager dataManager,
        IAddonOperationQueueStore queueStore,
        IAddonDependencyGraphStore graphStore,
        AddonResourceLockManager lockManager,
        int maxConcurrency = 2)
    {
        if (maxConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Max concurrency must be at least 1.");

        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
        _queueStore = queueStore ?? throw new ArgumentNullException(nameof(queueStore));
        _graphStore = graphStore ?? throw new ArgumentNullException(nameof(graphStore));
        _lockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
        _concurrencyGate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _signalChannel = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
        _executionContexts = new ConcurrentDictionary<Guid, QueuedOperationExecutionContext>();

        _ = SignalPersistedPendingOperationsAsync();
    }

    public Task<InstallResult> EnqueueInstallAsync(
        Addon addon,
        AddonInstallationMethod installationMethod,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return EnqueueAsync(
            addon, AddonOperationType.Install, installationMethod, recursive, null, progress, cancellationToken);
    }

    public async Task<QueuedOperationSubmissionResult> SubmitInstallAsync(
        Addon addon,
        AddonInstallationMethod installationMethod,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        QueuedOperationExecutionContext context;
        try
        {
            context = await EnqueueContextAsync(
                addon, AddonOperationType.Install, installationMethod, recursive, null,
                progress, CancellationToken.None);
        }
        catch (QueuedOperationRejectedException ex)
        {
            return QueuedOperationSubmissionResult.Rejected(ex.Message);
        }

        return QueuedOperationSubmissionResult.AcceptedOperation(context.Operation);
    }

    public Task<InstallResult> EnqueueUpdateAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return EnqueueAsync(
            addon, AddonOperationType.Update, addon.InstallationMethod, recursive, null, progress, cancellationToken);
    }

    public async Task<QueuedOperationSubmissionResult> SubmitUpdateAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        QueuedOperationExecutionContext context;
        try
        {
            context = await EnqueueContextAsync(
                addon, AddonOperationType.Update, addon.InstallationMethod, recursive, null,
                progress, CancellationToken.None);
        }
        catch (QueuedOperationRejectedException ex)
        {
            return QueuedOperationSubmissionResult.Rejected(ex.Message);
        }

        return QueuedOperationSubmissionResult.AcceptedOperation(context.Operation);
    }

    public Task<InstallResult> EnqueueReinstallAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return EnqueueAsync(
            addon, AddonOperationType.Reinstall, addon.InstallationMethod, recursive, null, progress, cancellationToken);
    }

    public async Task<QueuedOperationSubmissionResult> SubmitReinstallAsync(
        Addon addon,
        bool recursive,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        QueuedOperationExecutionContext context;
        try
        {
            context = await EnqueueContextAsync(
                addon, AddonOperationType.Reinstall, addon.InstallationMethod, recursive, null,
                progress, CancellationToken.None);
        }
        catch (QueuedOperationRejectedException ex)
        {
            return QueuedOperationSubmissionResult.Rejected(ex.Message);
        }

        return QueuedOperationSubmissionResult.AcceptedOperation(context.Operation);
    }

    public Task<InstallResult> EnqueueDeleteAsync(
        Addon addon,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return EnqueueDeleteAsync(addon, new DeleteOperationOptions(false), progress, cancellationToken);
    }

    public Task<QueuedOperationSubmissionResult> SubmitDeleteAsync(
        Addon addon,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return SubmitDeleteAsync(addon, new DeleteOperationOptions(false), progress, cancellationToken);
    }

    public Task<InstallResult> EnqueueDeleteAsync(
        Addon addon,
        DeleteOperationOptions options,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return EnqueueAsync(
            addon, AddonOperationType.Delete, addon.InstallationMethod, false,
            options, progress, cancellationToken);
    }

    public async Task<QueuedOperationSubmissionResult> SubmitDeleteAsync(
        Addon addon,
        DeleteOperationOptions options,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        QueuedOperationExecutionContext context;
        try
        {
            context = await EnqueueContextAsync(
                addon, AddonOperationType.Delete, addon.InstallationMethod, false,
                options, progress, CancellationToken.None);
        }
        catch (QueuedOperationRejectedException ex)
        {
            return QueuedOperationSubmissionResult.Rejected(ex.Message);
        }

        return QueuedOperationSubmissionResult.AcceptedOperation(context.Operation);
    }

    private async Task<InstallResult> EnqueueAsync(
        Addon addon,
        string operationType,
        AddonInstallationMethod installationMethod,
        bool recursive,
        DeleteOperationOptions? deleteOptions,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        QueuedOperationExecutionContext context;
        try
        {
            context = await EnqueueContextAsync(
                addon, operationType, installationMethod, recursive, deleteOptions,
                progress, cancellationToken);
        }
        catch (QueuedOperationRejectedException ex)
        {
            return InstallResult.Failure($"Operation rejected: {ex.Message}");
        }

        return await context.Completion.Task.ConfigureAwait(false);
    }

    private async Task<QueuedOperationExecutionContext> EnqueueContextAsync(
        Addon addon,
        string operationType,
        AddonInstallationMethod installationMethod,
        bool recursive,
        DeleteOperationOptions? deleteOptions,
        IProgress<InstallProgress>? progress,
        CancellationToken operationCancellationToken)
    {
        var operation = new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = addon.CommonAddonId,
            AddonName = addon.Name,
            OperationType = operationType,
            InstallationMethod = installationMethod,
            Recursive = recursive,
            DeleteOptions = deleteOptions,
            Priority = operationType == AddonOperationType.CleanupOrphans
                ? QueuePriority.Low
                : QueuePriority.Normal,
            Status = QueueOperationStatus.Pending,
            RequestTime = DateTime.UtcNow
        };

        var rejectionReason = await CheckEnqueueConflictsAsync(operation).ConfigureAwait(false);
        if (rejectionReason != null)
            throw new QueuedOperationRejectedException(rejectionReason);

        var tcs = new TaskCompletionSource<InstallResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Store the operation first. The caller's cancellation is handled after
        // all infrastructure is set up.
        await _queueStore.EnqueueAsync(operation, CancellationToken.None);

        var internalCts = new CancellationTokenSource();

        var context = new QueuedOperationExecutionContext
        {
            Operation = operation,
            Progress = progress,
            CancellationToken = operationCancellationToken,
            Completion = tcs,
            InternalCts = internalCts
        };

        _executionContexts.TryAdd(operation.OperationId, context);

        EnsureProcessing();

        // If the caller's token is already canceled, complete immediately.
        // Otherwise, register a handler for future cancellation.
        if (operationCancellationToken.IsCancellationRequested)
            context.Completion.TrySetResult(
                InstallResult.Canceled("Operation was canceled by user."));
        else if (operationCancellationToken.CanBeCanceled)
            operationCancellationToken.Register(() =>
            {
                if (!context.ExecutionStarted)
                    context.Completion.TrySetResult(
                        InstallResult.Canceled("Operation was canceled by user."));
            });

        // If the internal token was already triggered (e.g., queue cancel before start),
        // cancel immediately.
        if (internalCts.IsCancellationRequested)
            context.Completion.TrySetResult(
                InstallResult.Canceled("Operation was canceled via queue management."));

        await _signalChannel.Writer.WriteAsync(1, CancellationToken.None);

        return context;
    }

    private async Task<string?> CheckEnqueueConflictsAsync(QueuedOperation operation)
    {
        var operations = await _queueStore.GetAllAsync(CancellationToken.None).ConfigureAwait(false);

        foreach (var activeOperation in operations)
        {
            if (activeOperation.Status is not (QueueOperationStatus.Pending or QueueOperationStatus.InProgress))
                continue;

            if (activeOperation.AddonCommonId == operation.AddonCommonId ||
                string.Equals(activeOperation.AddonName, operation.AddonName, StringComparison.OrdinalIgnoreCase))
                return $"Addon '{activeOperation.AddonName}' already has a {activeOperation.Status} {activeOperation.OperationType} operation.";
        }

        if (operation.OperationType == AddonOperationType.Delete)
        {
            var snapshot = _graphStore.CreateSnapshot();
            return await CheckDeleteQueueConflictsAsync(operation, snapshot).ConfigureAwait(false);
        }

        return null;
    }

    private async Task SignalPersistedPendingOperationsAsync()
    {
        try
        {
            var operations = await _queueStore.GetAllAsync(CancellationToken.None).ConfigureAwait(false);
            var pendingCount = operations.Count(o => o.Status == QueueOperationStatus.Pending);

            if (pendingCount == 0)
                return;

            for (var i = 0; i < pendingCount; i++)
                await _signalChannel.Writer.WriteAsync(1, CancellationToken.None).ConfigureAwait(false);

            EnsureProcessing();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SignalPersistedPendingOperations] Exception: {ex.Message}");
        }
    }

    private void EnsureProcessing()
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0) _ = ProcessQueueLoopAsync();
    }

    private async Task ProcessQueueLoopAsync()
    {
        try
        {
            await foreach (var signal in _signalChannel.Reader.ReadAllAsync())
            {
                await _concurrencyGate.WaitAsync();

                _ = TryClaimAndExecuteAsync();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    private async Task TryClaimAndExecuteAsync()
    {
        QueuedOperationExecutionContext? context = null;

        try
        {
            var claimedOperation = await _queueStore.TryClaimNextAsync(CancellationToken.None);

            if (claimedOperation == null)
                return;

            if (!_executionContexts.TryGetValue(claimedOperation.OperationId, out context))
            {
                context = CreateRecoveryContext(claimedOperation);
                _executionContexts.TryAdd(claimedOperation.OperationId, context);
            }

            ApplyPersistedCancellationRequest(context, claimedOperation);

            // Check the caller's cancellation token, the internal CTS and any
            // persisted cancel request recovered from the queue store.
            if (context.CancellationToken.IsCancellationRequested ||
                context.InternalCts.IsCancellationRequested)
            {
                var reason = context.InternalCts.IsCancellationRequested
                    ? GetQueueCancellationReason(context.Operation)
                    : "Operation was canceled by user.";

                var result = InstallResult.Canceled(reason);
                await CompleteQueuedOperationAsync(context, QueueOperationStatus.Canceled, result)
                    .ConfigureAwait(false);

                _executionContexts.TryRemove(claimedOperation.OperationId, out _);
                return;
            }

            context.ExecutionStarted = true;

            // Create a linked token that fires when either the user cancels
            // or the internal CTS is triggered (e.g., via queue cancel command).
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                context.CancellationToken, context.InternalCts.Token);
            var linkedCt = linkedCts.Token;

            using var cancellationWatcherCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCt);
            var cancellationWatcherTask = WatchPersistedCancellationRequestsAsync(
                context, cancellationWatcherCts.Token);

            try
            {
                // Build request from the queued operation
                var request = BuildRequest(context.Operation);

                // Build initial plan outside locks to get resource keys
                var snapshot = _graphStore.CreateSnapshot();
                var plan = AddonOperationPlanner.BuildPlan(request, snapshot, ResolveAddonName);

                if (plan.IsRejected)
                {
                    var rejectionResult = InstallResult.Failure($"Operation rejected: {plan.RejectionReason}");
                    await CompleteQueuedOperationAsync(context, QueueOperationStatus.Failed, rejectionResult)
                        .ConfigureAwait(false);

                    _executionContexts.TryRemove(claimedOperation.OperationId, out _);
                    return;
                }

                // Queue-aware validation: check pending operations in the queue
                if (request.OperationType == AddonOperationType.Delete)
                {
                    var queueConflict = await CheckDeleteQueueConflictsAsync(context.Operation, snapshot);
                    if (queueConflict != null)
                    {
                        var conflictResult = InstallResult.Failure($"Delete rejected: {queueConflict}");
                        await CompleteQueuedOperationAsync(context, QueueOperationStatus.Failed, conflictResult)
                            .ConfigureAwait(false);

                        _executionContexts.TryRemove(claimedOperation.OperationId, out _);
                        return;
                    }
                }

                // For CleanupOrphans operations, use a simplified execution path
                if (request.OperationType == AddonOperationType.CleanupOrphans)
                {
                    await ExecuteCleanupOrphansAsync(context, snapshot, linkedCt);
                    return;
                }

                // Attempt to acquire locks and execute, with replanning under locks
                var result = await ExecutePlanWithLocksAsync(context, plan, request, linkedCt);

                // After a successful root delete with cascade, enqueue deferred orphan cleanup
                if (result.Succeeded &&
                    request.OperationType == AddonOperationType.Delete &&
                    context.Operation.DeleteOptions?.RemoveOrphanedDependencies == true)
                    _ = EnqueueCleanupOrphansAsync(request.RootCommonAddonId, request.RootAddonName);
            }
            finally
            {
                cancellationWatcherCts.Cancel();
                await ObserveCancellationWatcherCompletionAsync(cancellationWatcherTask);
            }
        }
        catch (OperationCanceledException)
        {
            if (context != null)
            {
                const string message = "Operation was canceled.";
                await CompleteQueuedOperationAsync(
                        context, QueueOperationStatus.Canceled, InstallResult.Canceled(message))
                    .ConfigureAwait(false);

                _executionContexts.TryRemove(context.Operation.OperationId, out _);
            }
        }
        catch (Exception ex)
        {
            // Surface unexpected errors to the caller instead of silently dropping them
            Debug.WriteLine(ex);

            if (context != null)
            {
                await CompleteQueuedOperationAsync(
                        context,
                        QueueOperationStatus.Failed,
                        InstallResult.Failure($"Unexpected error during {context.Operation.OperationType}: {ex.Message}"))
                    .ConfigureAwait(false);

                _executionContexts.TryRemove(context.Operation.OperationId, out _);
            }
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private async Task<InstallResult> ExecutePlanWithLocksAsync(
        QueuedOperationExecutionContext context,
        AddonOperationPlan initialPlan,
        AddonOperationRequest request,
        CancellationToken ct)
    {
        var plan = initialPlan;

        for (var retry = 0; retry < MaxReplanRetries; retry++)
        {
            ct.ThrowIfCancellationRequested();

            // Acquire locks with CancellationToken.None (original design pattern).
            // Cancellation is checked before and after acquisition.
            using (await _lockManager.AcquireAsync(plan.Resources, CancellationToken.None))
            {
                ct.ThrowIfCancellationRequested();

                // Under locks, rebuild the snapshot and replan
                var freshSnapshot = _graphStore.CreateSnapshot();
                var replan = AddonOperationPlanner.BuildPlan(request, freshSnapshot, ResolveAddonName);

                if (replan.IsRejected)
                {
                    var result = InstallResult.Failure(
                        $"Operation rejected after replan: {replan.RejectionReason}");
                    await CompleteQueuedOperationAsync(context, QueueOperationStatus.Failed, result)
                        .ConfigureAwait(false);

                    _executionContexts.TryRemove(context.Operation.OperationId, out _);

                    return result;
                }

                // If the resource set changed, release and retry with new resources
                if (!plan.Resources.SetEquals(replan.Resources))
                {
                    plan = replan;
                    continue; // `using` disposes the lock, loop retries
                }

                // Execute the plan actions in order
                await ExecutePlanActionsAsync(context, replan, ct);

                // Extract the result from the completion source
                return await context.Completion.Task;
            }
        }

        // If we exhausted retries, fail
        var failResult = InstallResult.Failure(
            $"Operation {context.Operation.OperationType} failed: " +
            "dependency graph changed too frequently. Please try again.");

        await CompleteQueuedOperationAsync(context, QueueOperationStatus.Failed, failResult)
            .ConfigureAwait(false);

        _executionContexts.TryRemove(context.Operation.OperationId, out _);

        return failResult;
    }

    private async Task CompleteQueuedOperationAsync(
        QueuedOperationExecutionContext context,
        QueueOperationStatus status,
        InstallResult result)
    {
        var operation = context.Operation;

        operation.Status = status;
        operation.CompletionTime = DateTime.UtcNow;
        operation.ErrorMessage = result.ErrorMessage;
        operation.ResultMessage = result.WarningMessage;
        operation.CompletedAfterCancellation = result.CompletedAfterCancellation;

        if (result.WarningMessage != null || result.CompletedAfterCancellation)
        {
            await _queueStore.UpdateResultAsync(
                    operation.OperationId,
                    result.WarningMessage,
                    result.CompletedAfterCancellation,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        await _queueStore.UpdateStatusAsync(
                operation.OperationId,
                status,
                result.ErrorMessage,
                CancellationToken.None)
            .ConfigureAwait(false);

        context.Completion.TrySetResult(result);
    }

    private async Task ExecutePlanActionsAsync(
        QueuedOperationExecutionContext context,
        AddonOperationPlan plan,
        CancellationToken cancellationToken)
    {
        var operation = context.Operation;
        operation.Status = QueueOperationStatus.InProgress;
        operation.StartTime = DateTime.UtcNow;
        string? warningMessage = null;
        var completedAfterCancellation = false;

        // Execute actions in execution order
        foreach (var action in plan.Actions.OrderBy(a => a.ExecutionOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var addon = FindAddon(action.CommonAddonId, action.AddonName);

            if (addon == null)
            {
                var result = InstallResult.Failure(
                    $"Addon '{action.AddonName}' (CommonAddonId={action.CommonAddonId}) not found.");

                operation.Status = QueueOperationStatus.Failed;
                operation.CompletionTime = DateTime.UtcNow;
                operation.ErrorMessage = result.ErrorMessage;

                await CompleteQueuedOperationAsync(context, QueueOperationStatus.Failed, result)
                    .ConfigureAwait(false);
                return;
            }

            if (action.OperationType == AddonOperationType.Delete &&
                !action.IsRoot &&
                addon.InstallationMethod != AddonInstallationMethod.Dependency)
            {
                Debug.WriteLine(
                    $"Skipping auto-delete of '{addon.Name}' because it was installed as {addon.InstallationMethod}.");
                continue;
            }

            InstallResult actionResult;

            try
            {
                actionResult = await ExecuteWorkerActionAsync(
                    addon, action, context.Operation.InstallationMethod, context.Progress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                actionResult = InstallResult.Canceled($"Operation was canceled during {action.OperationType}.");
            }

            if (!actionResult.Succeeded && !actionResult.WasCanceled)
            {
                // Action failed — stop the plan
                operation.Status = QueueOperationStatus.Failed;
                operation.CompletionTime = DateTime.UtcNow;
                operation.ErrorMessage = actionResult.ErrorMessage;

                await CompleteQueuedOperationAsync(context, QueueOperationStatus.Failed, actionResult)
                    .ConfigureAwait(false);
                return;
            }

            if (actionResult.WasCanceled)
            {
                operation.Status = QueueOperationStatus.Canceled;
                operation.CompletionTime = DateTime.UtcNow;

                await CompleteQueuedOperationAsync(context, QueueOperationStatus.Canceled, actionResult)
                    .ConfigureAwait(false);
                return;
            }

            warningMessage ??= actionResult.WarningMessage;
            completedAfterCancellation |= actionResult.CompletedAfterCancellation;
        }

        // All actions succeeded.
        // First phase: mark all journal/worker work as complete.
        await _queueStore.MarkJournalCompletedAsync(operation.OperationId, CancellationToken.None);

        // Second phase: update the queue status to Completed.
        operation.Status = QueueOperationStatus.Completed;
        operation.CompletionTime = DateTime.UtcNow;

        var successResult = InstallResult.Success(warningMessage, completedAfterCancellation);
        await CompleteQueuedOperationAsync(context, QueueOperationStatus.Completed, successResult)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Enqueues a low-priority CleanupOrphans operation.
    /// Fire-and-forget: runs in the background when no higher-priority operations are pending.
    /// </summary>
    private async Task EnqueueCleanupOrphansAsync(int rootCommonAddonId, string rootAddonName)
    {
        try
        {
            var cleanupOp = new QueuedOperation
            {
                OperationId = Guid.NewGuid(),
                AddonCommonId = rootCommonAddonId,
                AddonName = rootAddonName,
                OperationType = AddonOperationType.CleanupOrphans,
                Status = QueueOperationStatus.Pending,
                Priority = QueuePriority.Low,
                RequestTime = DateTime.UtcNow
            };

            var tcs = new TaskCompletionSource<InstallResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var context = new QueuedOperationExecutionContext
            {
                Operation = cleanupOp,
                Completion = tcs
            };

            await _queueStore.EnqueueAsync(cleanupOp, CancellationToken.None);
            _executionContexts.TryAdd(cleanupOp.OperationId, context);
            await _signalChannel.Writer.WriteAsync(1, CancellationToken.None);
            EnsureProcessing();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EnqueueCleanupOrphans] Exception: {ex.Message}");
        }
    }

    private static QueuedOperationExecutionContext CreateRecoveryContext(QueuedOperation operation)
    {
        return new QueuedOperationExecutionContext
        {
            Operation = operation,
            CancellationToken = CancellationToken.None,
            Completion = new TaskCompletionSource<InstallResult>(
                TaskCreationOptions.RunContinuationsAsynchronously),
            InternalCts = new CancellationTokenSource()
        };
    }

    private static void ApplyPersistedCancellationRequest(
        QueuedOperationExecutionContext context,
        QueuedOperation persistedOperation)
    {
        if (!persistedOperation.CancelRequested)
            return;

        context.Operation.CancelRequested = true;
        context.Operation.CancelReason = persistedOperation.CancelReason;
        context.Operation.CancelRequestedAtUtc = persistedOperation.CancelRequestedAtUtc;
        context.InternalCts.Cancel();
    }

    private static string GetQueueCancellationReason(QueuedOperation operation)
    {
        return string.IsNullOrWhiteSpace(operation.CancelReason)
            ? QueueCancellationMessage
            : operation.CancelReason;
    }

    private async Task WatchPersistedCancellationRequestsAsync(
        QueuedOperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PersistedCancellationPollInterval, cancellationToken)
                    .ConfigureAwait(false);

                var operations = await _queueStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
                var operation = operations.FirstOrDefault(o => o.OperationId == context.Operation.OperationId);

                if (operation?.CancelRequested != true)
                    continue;

                ApplyPersistedCancellationRequest(context, operation);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CancellationWatcher] Exception: {ex.Message}");
                return;
            }
        }
    }

    private static async Task ObserveCancellationWatcherCompletionAsync(Task watcherTask)
    {
        try
        {
            await watcherTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the operation completes before another cancellation request is persisted.
        }
    }

    private sealed class QueuedOperationRejectedException(string message) : Exception(message);

    /// <summary>
    /// Executes a CleanupOrphans operation: finds dependencies of the deleted root
    /// addon that now have zero reverse dependents and removes them.
    /// </summary>
    private async Task ExecuteCleanupOrphansAsync(
        QueuedOperationExecutionContext context,
        AddonGraphSnapshot snapshot,
        CancellationToken ct)
    {
        try
        {
            var orphans = FindOrphanCleanupCandidates(snapshot, context.Operation.AddonCommonId);

            Debug.WriteLine($"[CleanupOrphans] Found {orphans.Count} orphan candidates");

            if (orphans.Count == 0)
            {
                await CompleteQueuedOperationAsync(
                        context,
                        QueueOperationStatus.Completed,
                        InstallResult.Success("No orphaned addons to clean up."))
                    .ConfigureAwait(false);

                _executionContexts.TryRemove(context.Operation.OperationId, out _);
                return;
            }

            var resources = BuildResourceKeys(orphans);

            using (await _lockManager.AcquireAsync(resources, CancellationToken.None))
            {
                ct.ThrowIfCancellationRequested();

                var freshSnapshot = _graphStore.CreateSnapshot();
                orphans = FindOrphanCleanupCandidates(freshSnapshot, context.Operation.AddonCommonId);

                Debug.WriteLine($"[CleanupOrphans] Found {orphans.Count} orphan candidates after locking");

                if (orphans.Count == 0)
                {
                    await CompleteQueuedOperationAsync(
                            context,
                            QueueOperationStatus.Completed,
                            InstallResult.Success("No orphaned addons to clean up."))
                        .ConfigureAwait(false);

                    _executionContexts.TryRemove(context.Operation.OperationId, out _);
                    return;
                }

                // Execute delete for each orphan while holding their resource locks.
                foreach (var orphan in orphans)
                {
                    ct.ThrowIfCancellationRequested();

                    var action = new PlannedAddonAction(
                        orphan.CommonAddonId, orphan.Name, AddonOperationType.Delete, 0, false);

                    var result = await ExecuteWorkerActionAsync(
                        orphan, action, orphan.InstallationMethod, null, ct);

                    if (!result.Succeeded && !result.WasCanceled)
                    {
                        // Stop on first failure
                        await CompleteQueuedOperationAsync(context, QueueOperationStatus.Failed, result)
                            .ConfigureAwait(false);

                        _executionContexts.TryRemove(context.Operation.OperationId, out _);
                        return;
                    }

                    if (result.WasCanceled)
                    {
                        await CompleteQueuedOperationAsync(context, QueueOperationStatus.Canceled, result)
                            .ConfigureAwait(false);

                        _executionContexts.TryRemove(context.Operation.OperationId, out _);
                        return;
                    }
                }
            }

            // All orphans cleaned up.
            // Mark journal complete before updating queue status.
            await _queueStore.MarkJournalCompletedAsync(context.Operation.OperationId, CancellationToken.None);

            await CompleteQueuedOperationAsync(
                    context,
                    QueueOperationStatus.Completed,
                    InstallResult.Success())
                .ConfigureAwait(false);

            _executionContexts.TryRemove(context.Operation.OperationId, out _);
        }
        catch (OperationCanceledException)
        {
            await CompleteQueuedOperationAsync(
                    context,
                    QueueOperationStatus.Canceled,
                    InstallResult.Canceled("Orphan cleanup was canceled."))
                .ConfigureAwait(false);

            _executionContexts.TryRemove(context.Operation.OperationId, out _);
        }
        catch (Exception ex)
        {
            await CompleteQueuedOperationAsync(
                    context,
                    QueueOperationStatus.Failed,
                    InstallResult.Failure($"Orphan cleanup failed: {ex.Message}"))
                .ConfigureAwait(false);

            _executionContexts.TryRemove(context.Operation.OperationId, out _);
        }
    }

    private List<Addon> FindOrphanCleanupCandidates(AddonGraphSnapshot snapshot, int rootCommonAddonId)
    {
        var orphans = new List<Addon>();
        var candidateAddonIds = snapshot.GetClosureCombined(rootCommonAddonId);

        Debug.WriteLine($"[CleanupOrphans] Scanning {candidateAddonIds.Count} dependency candidate IDs");

        foreach (var addonId in candidateAddonIds)
        {
            // An addon is an orphan if it was in the deleted root's dependency
            // closure and now has zero reverse dependents in the installed graph.
            var reverseDeps = snapshot.ReverseLocalDependencies.TryGetValue(addonId, out var rev)
                ? rev.Count
                : 0;

            Debug.WriteLine($"[CleanupOrphans] AddonId={addonId}, reverseDeps={reverseDeps}");

            if (reverseDeps > 0)
                continue;

            var addon = _dataManager.InstalledAddons
                .Concat(_dataManager.OnlineAddons)
                .FirstOrDefault(a => a.CommonAddonId == addonId);

            if (addon == null)
            {
                Debug.WriteLine($"[CleanupOrphans] AddonId={addonId} not found in data manager, skipping");
                continue;
            }

            if (addon.InstallationMethod != AddonInstallationMethod.Dependency)
            {
                Debug.WriteLine(
                    $"[CleanupOrphans] AddonId={addonId} installed as {addon.InstallationMethod}, skipping");
                continue;
            }

            Debug.WriteLine($"[CleanupOrphans] Found candidate: {addon.Name} (Id={addonId})");
            orphans.Add(addon);
        }

        return orphans;
    }

    private static IReadOnlySet<AddonResourceKey> BuildResourceKeys(IEnumerable<Addon> addons)
    {
        var resources = new HashSet<AddonResourceKey>();

        foreach (var addon in addons)
        {
            resources.Add(AddonResourceKey.ForAddon(addon.CommonAddonId));
            resources.Add(AddonResourceKey.ForFolder(addon.Name));
        }

        return resources;
    }

    private static AddonOperationRequest BuildRequest(QueuedOperation operation)
    {
        return new AddonOperationRequest(
            operation.OperationType,
            operation.AddonCommonId,
            operation.AddonName,
            operation.Recursive,
            operation.InstallationMethod,
            operation.OperationType == AddonOperationType.Delete
                ? operation.DeleteOptions ?? new DeleteOperationOptions(false)
                : null);
    }

    private Addon? FindAddon(int commonAddonId, string name)
    {
        return _dataManager.OnlineAddons.FirstOrDefault(a => a.CommonAddonId == commonAddonId)
               ?? _dataManager.InstalledAddons.FirstOrDefault(a => a.CommonAddonId == commonAddonId);
    }

    private string ResolveAddonName(int commonAddonId)
    {
        return _dataManager.OnlineAddons.FirstOrDefault(a => a.CommonAddonId == commonAddonId)?.Name
               ?? _dataManager.InstalledAddons.FirstOrDefault(a => a.CommonAddonId == commonAddonId)?.Name
               ?? commonAddonId.ToString();
    }

    /// <summary>
    /// Checks if there are pending or in-progress operations in the queue that would
    /// conflict with a delete operation. Returns null if no conflict, or an error message.
    /// </summary>
    private async Task<string?> CheckDeleteQueueConflictsAsync(
        QueuedOperation deleteOperation,
        AddonGraphSnapshot snapshot)
    {
        var targetAddonId = deleteOperation.AddonCommonId;

        var allOps = await _queueStore.GetAllAsync(CancellationToken.None);

        foreach (var pendingOp in allOps)
        {
            if (pendingOp.Status != QueueOperationStatus.Pending &&
                pendingOp.Status != QueueOperationStatus.InProgress)
                continue;

            if (pendingOp.OperationId == deleteOperation.OperationId)
                continue; // skip self

            // Direct conflict: pending operation targets the same addon
            if (pendingOp.AddonCommonId == targetAddonId)
            {
                var pendingName = pendingOp.AddonName ?? pendingOp.AddonCommonId.ToString();
                return $"Addon '{pendingName}' has a pending {pendingOp.OperationType} operation.";
            }

            // Dependency conflict: pending operation's addon depends on the addon being deleted.
            // For pending delete/cleanup-orphans operations, the dependency direction is reversed:
            // the pending delete would also be removing the target, which is harmless.
            if (pendingOp.OperationType != AddonOperationType.Delete &&
                pendingOp.OperationType != AddonOperationType.CleanupOrphans)
            {
                var pendingClosure = snapshot.GetClosureCombined(pendingOp.AddonCommonId);
                if (pendingClosure.Contains(targetAddonId))
                {
                    var pendingName = ResolveAddonName(pendingOp.AddonCommonId);
                    return $"'{pendingName}' has a pending {pendingOp.OperationType} operation that depends on '{deleteOperation.AddonName}'.";
                }
            }
        }

        return null;
    }

    public async Task CancelOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requested = await _queueStore.RequestCancellationAsync(
            operationId, QueueCancellationMessage, cancellationToken);

        if (!requested)
            return;

        if (_executionContexts.TryGetValue(operationId, out var context))
        {
            context.Operation.CancelRequested = true;
            context.Operation.CancelReason = QueueCancellationMessage;
            context.Operation.CancelRequestedAtUtc ??= DateTime.UtcNow;
            context.InternalCts.Cancel();
        }

        var operations = await _queueStore.GetAllAsync(CancellationToken.None);
        var target = operations.FirstOrDefault(o => o.OperationId == operationId);

        if (target?.Status == QueueOperationStatus.Canceled)
        {
            context?.Completion.TrySetResult(InstallResult.Canceled(GetQueueCancellationReason(target)));
            _executionContexts.TryRemove(operationId, out _);
        }
    }

    public async Task<IReadOnlyList<QueuedOperation>> GetAllQueuedOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _queueStore.GetAllAsync(cancellationToken);
    }

    private async Task<InstallResult> ExecuteWorkerActionAsync(
        Addon addon,
        PlannedAddonAction action,
        AddonInstallationMethod installationMethod,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        switch (action.OperationType)
        {
            case AddonOperationType.Install:
                return await _worker.InstallOneAsync(
                    addon, installationMethod, progress, cancellationToken);

            case AddonOperationType.Update:
            case AddonOperationType.Reinstall:
                return await _worker.ReplaceOneAsync(
                    addon, action.OperationType, installationMethod, progress, cancellationToken);

            case AddonOperationType.Delete:
                return _worker.DeleteOne(addon, progress, cancellationToken);

            default:
                return InstallResult.Failure($"Unknown operation type: {action.OperationType}");
        }
    }
}
