using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Data;

namespace SpellCrafter.Services;

internal static class AddonServices
{
    internal const string OperationExecutorLeaseUnavailableMessage =
        "Another SpellCrafter process is currently managing addon operations. Close it or wait until it finishes.";

    private static readonly TimeSpan OperationExecutorLeaseTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OperationExecutorLeaseRenewInterval = TimeSpan.FromSeconds(5);
    private static readonly string OperationExecutorOwnerId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    private static readonly object OperationExecutorLeaseSync = new();
    private static readonly SemaphoreSlim OperationExecutorLeaseSemaphore = new(1, 1);
    private static OperationExecutorLeaseHandle? _operationExecutorLease;
    private static Timer? _operationExecutorLeaseRenewTimer;
    private static bool _operationExecutorLeaseProcessExitRegistered;

    private static readonly Lazy<IEsoDataConnectionFactory> DataConnectionFactoryLazy =
        new(() => new EsoDataConnectionFactory());

    private static readonly Lazy<IAddonOperationJournalStore> JournalStoreLazy =
        new(() => new AddonOperationJournalStoreAdapter(DataConnectionFactory));

    private static readonly Lazy<IAddonDataManager> DataManagerLazy =
        new(() => new AddonDataManagerAdapter(DataConnectionFactory));

    private static readonly Lazy<IArchiveDownloader> ArchiveDownloaderLazy =
        new(() => new OnlineAddonsParserService());

    private static readonly Lazy<IAddonInstallationService> InstallationServiceLazy =
        new(() => new AddonInstallationService(
            JournalStore,
            DataManager,
            ArchiveDownloader,
            DataConnectionFactory));

    private static readonly Lazy<ISingleAddonInstallationWorker> WorkerLazy =
        new(() => new SingleAddonInstallationWorker(
            JournalStore, DataManager, ArchiveDownloader, DataConnectionFactory));

    private static readonly Lazy<IAddonOperationQueueStore> QueueStoreLazy =
        new(() => new SqliteAddonOperationQueueStore(DataConnectionFactory));

    private static readonly Lazy<IAddonOperationLeaseStore> LeaseStoreLazy =
        new(() => new SqliteAddonOperationLeaseStore(DataConnectionFactory));

    private static readonly Lazy<AddonResourceLockManager> LockManagerLazy =
        new(() => new AddonResourceLockManager());

    private static readonly Lazy<IAddonDependencyGraphStore> GraphStoreLazy =
        new(() => new AddonDependencyGraphStore(DataConnectionFactory));

    private static readonly Lazy<IAddonOperationCoordinator> CoordinatorLazy =
        new(() => new AddonOperationCoordinator(Worker, DataManager, QueueStore, GraphStore, LockManager));

    private static readonly Lazy<IAddonInstallationService> QueuedInstallationServiceLazy =
        new(() => new QueuedAddonInstallationService(() => Coordinator));

    private static readonly Lazy<IAddonOperationSubmissionService> OperationSubmissionServiceLazy =
        new(() => new QueuedAddonOperationSubmissionService(GetOperationSubmissionCoordinatorAsync));

    internal static IEsoDataConnectionFactory DataConnectionFactory => DataConnectionFactoryLazy.Value;

    internal static IAddonOperationJournalStore JournalStore => JournalStoreLazy.Value;

    internal static IAddonDataManager DataManager => DataManagerLazy.Value;

    internal static IArchiveDownloader ArchiveDownloader => ArchiveDownloaderLazy.Value;

    internal static IAddonInstallationService InstallationService => QueuedInstallationServiceLazy.Value;

    internal static ISingleAddonInstallationWorker Worker => WorkerLazy.Value;

    internal static IAddonOperationQueueStore QueueStore => QueueStoreLazy.Value;

    internal static IAddonOperationLeaseStore LeaseStore => LeaseStoreLazy.Value;

    internal static AddonResourceLockManager LockManager => LockManagerLazy.Value;

    internal static IAddonDependencyGraphStore GraphStore => GraphStoreLazy.Value;

    internal static IAddonOperationCoordinator Coordinator
    {
        get
        {
            if (!TryAcquireOperationExecutorLease(out _))
                throw new InvalidOperationException(OperationExecutorLeaseUnavailableMessage);

            return CoordinatorLazy.Value;
        }
    }

    internal static IAddonInstallationService QueuedInstallationService => QueuedInstallationServiceLazy.Value;

    internal static IAddonOperationSubmissionService OperationSubmissionService => OperationSubmissionServiceLazy.Value;

    private static IAddonOperationSubmissionService OperationSubmissionCoordinator =>
        Coordinator is IAddonOperationSubmissionService submissionService
            ? submissionService
            : throw new InvalidOperationException(
                "Addon operation coordinator does not support operation submission.");

    private static async Task<IAddonOperationSubmissionService> GetOperationSubmissionCoordinatorAsync(
        CancellationToken cancellationToken)
    {
        var (acquired, errorMessage) = await TryAcquireOperationExecutorLeaseAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!acquired)
            throw new InvalidOperationException(errorMessage ?? OperationExecutorLeaseUnavailableMessage);

        return CoordinatorLazy.Value is IAddonOperationSubmissionService submissionService
            ? submissionService
            : throw new InvalidOperationException(
                "Addon operation coordinator does not support operation submission.");
    }

    internal static bool TryAcquireOperationExecutorLease(out string? errorMessage)
    {
        if (_operationExecutorLease != null)
        {
            errorMessage = null;
            return true;
        }

        lock (OperationExecutorLeaseSync)
        {
            _operationExecutorLease ??= AcquireOperationExecutorLease();
        }

        if (_operationExecutorLease == null)
        {
            errorMessage = OperationExecutorLeaseUnavailableMessage;
            return false;
        }

        errorMessage = null;
        return true;
    }

    internal static async Task<(bool Acquired, string? ErrorMessage)> TryAcquireOperationExecutorLeaseAsync(
        CancellationToken cancellationToken = default)
    {
        if (_operationExecutorLease != null)
            return (true, null);

        await OperationExecutorLeaseSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _operationExecutorLease ??= await AcquireOperationExecutorLeaseAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            OperationExecutorLeaseSemaphore.Release();
        }

        return _operationExecutorLease == null
            ? (false, OperationExecutorLeaseUnavailableMessage)
            : (true, null);
    }

    private static OperationExecutorLeaseHandle? AcquireOperationExecutorLease()
    {
        var handle = LeaseStore.TryAcquireAsync(
                "process",
                OperationExecutorOwnerId,
                OperationExecutorLeaseTtl)
            .GetAwaiter()
            .GetResult();

        if (handle == null)
            return null;

        StartOperationExecutorLeaseRenewal();
        RegisterOperationExecutorLeaseProcessExitRelease();

        return handle;
    }

    private static async Task<OperationExecutorLeaseHandle?> AcquireOperationExecutorLeaseAsync(
        CancellationToken cancellationToken)
    {
        var handle = await LeaseStore.TryAcquireAsync(
                "process",
                OperationExecutorOwnerId,
                OperationExecutorLeaseTtl,
                cancellationToken)
            .ConfigureAwait(false);

        if (handle == null)
            return null;

        StartOperationExecutorLeaseRenewal();
        RegisterOperationExecutorLeaseProcessExitRelease();
        return handle;
    }

    private static void RegisterOperationExecutorLeaseProcessExitRelease()
    {
        if (_operationExecutorLeaseProcessExitRegistered)
            return;

        _operationExecutorLeaseProcessExitRegistered = true;

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                _operationExecutorLeaseRenewTimer?.Dispose();
                LeaseStore.ReleaseAsync(OperationExecutorOwnerId).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OperationExecutorLease] Release failed: {ex.Message}");
            }
        };
    }

    private static void StartOperationExecutorLeaseRenewal()
    {
        _operationExecutorLeaseRenewTimer?.Dispose();
        _operationExecutorLeaseRenewTimer = new Timer(
            _ => _ = RenewOperationExecutorLeaseAsync(),
            null,
            OperationExecutorLeaseRenewInterval,
            OperationExecutorLeaseRenewInterval);
    }

    private static void RenewOperationExecutorLease()
    {
        RenewOperationExecutorLeaseAsync().GetAwaiter().GetResult();
    }

    private static async Task RenewOperationExecutorLeaseAsync()
    {
        try
        {
            _ = await LeaseStore.RenewAsync(OperationExecutorOwnerId, OperationExecutorLeaseTtl)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OperationExecutorLease] Renew failed: {ex.Message}");
        }
    }
}
