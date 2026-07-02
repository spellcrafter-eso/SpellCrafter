using System.Reactive.Concurrency;
using ReactiveUI;
using SpellCrafter.Enums;
using SpellCrafter.Models;
using SpellCrafter.Services;
using SpellCrafter.ViewModels;

namespace SpellCrafter.Tests.UnitTests;

public sealed class AddonsOverviewViewModelTests : IDisposable
{
    private readonly InMemoryOperationQueueStore _queueStore;

    public AddonsOverviewViewModelTests()
    {
        _queueStore = new InMemoryOperationQueueStore();
        // Use ImmediateScheduler to ensure Schedule executes synchronously in tests.
        // RxSchedulers.MainThreadScheduler resolves to DefaultScheduler in the test
        // environment, which queues actions asynchronously.
        AddonsScheduler.MainThread = ImmediateScheduler.Instance;
        // Disable the periodic queue refresh to avoid scheduler issues in test environment.
        // The timer uses Observable.Interval which requires a scheduler with timing support.
        AddonsTestHooks.DisablePeriodicRefresh = true;
    }

    public void Dispose()
    {
        AddonsScheduler.MainThread = RxSchedulers.MainThreadScheduler;
        AddonsTestHooks.DisablePeriodicRefresh = false;
    }

    [Fact]
    public async Task RefreshQueueStateAsync_WhenActiveOperationExists_SetsAddonQueuedOperation()
    {
        var addon = new Addon
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.NotInstalled
        };
        var modsSource = new RangedObservableCollection<Addon> { addon };

        var vm = new TestAddonsOverviewViewModel();
        vm.SetModsSource(modsSource);

        var operation = new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = addon.CommonAddonId,
            AddonName = addon.Name,
            OperationType = AddonOperationType.Install,
            Status = QueueOperationStatus.Pending,
            Priority = QueuePriority.Normal,
            RequestTime = DateTime.UtcNow
        };
        await _queueStore.EnqueueAsync(operation);

        vm.SetQueueStore(_queueStore);
        await vm.RefreshQueueStateAsync();

        Assert.True(addon.HasQueuedOperation,
            $"Expected HasQueuedOperation=true but got false. " +
            $"QueuedOperationId={addon.QueuedOperationId}, " +
            $"QueuedOperationStatus={addon.QueuedOperationStatus}, " +
            $"QueuedOperationDisplayText='{addon.QueuedOperationDisplayText}', " +
            $"QueuedOperationCancelRequested={addon.QueuedOperationCancelRequested}");
        Assert.Equal(operation.OperationId, addon.QueuedOperationId);
        Assert.Equal(QueueOperationStatus.Pending, addon.QueuedOperationStatus);
        Assert.Equal("Pending install", addon.QueuedOperationDisplayText);
    }

    [Fact]
    public async Task RefreshQueueStateAsync_WhenInProgressAndPendingExistForSameAddon_PrefersInProgress()
    {
        var addon = new Addon
        {
            CommonAddonId = 100,
            Name = "TestAddon",
            State = AddonState.NotInstalled
        };
        var modsSource = new RangedObservableCollection<Addon> { addon };

        var vm = new TestAddonsOverviewViewModel();
        vm.SetModsSource(modsSource);

        var pendingOp = new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = addon.CommonAddonId,
            AddonName = addon.Name,
            OperationType = AddonOperationType.Update,
            Status = QueueOperationStatus.Pending,
            Priority = QueuePriority.Normal,
            RequestTime = DateTime.UtcNow.AddSeconds(-10)
        };
        var inProgressOp = new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = addon.CommonAddonId,
            AddonName = addon.Name,
            OperationType = AddonOperationType.Install,
            Status = QueueOperationStatus.InProgress,
            Priority = QueuePriority.Normal,
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            RequestTime = DateTime.UtcNow
        };

        await _queueStore.EnqueueAsync(pendingOp);
        await _queueStore.EnqueueAsync(inProgressOp);

        vm.SetQueueStore(_queueStore);
        await vm.RefreshQueueStateAsync();

        Assert.True(addon.HasQueuedOperation,
            $"Expected HasQueuedOperation=true but got false. " +
            $"QueuedOperationId={addon.QueuedOperationId}, " +
            $"QueuedOperationStatus={addon.QueuedOperationStatus}, " +
            $"QueuedOperationDisplayText='{addon.QueuedOperationDisplayText}', " +
            $"QueuedOperationCancelRequested={addon.QueuedOperationCancelRequested}");
        Assert.Equal(inProgressOp.OperationId, addon.QueuedOperationId);
        Assert.Equal(QueueOperationStatus.InProgress, addon.QueuedOperationStatus);
    }

    [Fact]
    public async Task RefreshQueueStateAsync_WhenNoActiveOperation_ClearsAddonQueuedOperation()
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
            Status = QueueOperationStatus.Pending,
            RequestTime = DateTime.UtcNow
        });
        Assert.True(addon.HasQueuedOperation);

        var modsSource = new RangedObservableCollection<Addon> { addon };

        var vm = new TestAddonsOverviewViewModel();
        vm.SetModsSource(modsSource);

        // Queue store is empty - no active operations
        vm.SetQueueStore(_queueStore);
        await vm.RefreshQueueStateAsync();

        Assert.False(addon.HasQueuedOperation);
        Assert.Null(addon.QueuedOperationId);
        Assert.True(addon.InstallCommand.CanExecute(null));
    }

    private sealed class TestAddonsOverviewViewModel : AddonsOverviewViewModel
    {
        private IAddonOperationQueueStore? _queueStore;

        public TestAddonsOverviewViewModel()
            : base(false)
        {
        }

        public void SetModsSource(RangedObservableCollection<Addon> source)
        {
            SetModsSourceForTesting(source);
        }

        public void SetQueueStore(IAddonOperationQueueStore store) => _queueStore = store;

        internal override IAddonOperationQueueStore GetQueueStore() =>
            _queueStore ?? base.GetQueueStore();
    }
}
