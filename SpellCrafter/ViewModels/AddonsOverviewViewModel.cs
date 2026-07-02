using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SpellCrafter.Enums;
using SpellCrafter.Models;
using SpellCrafter.Services;

namespace SpellCrafter.ViewModels;

// Test seam: allows tests to override the main thread scheduler without Avalonia dispatcher.
// Defaults to RxSchedulers.MainThreadScheduler which is set by Avalonia platform initialization.
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
internal static class AddonsScheduler
{
    public static IScheduler MainThread { get; set; } = RxSchedulers.MainThreadScheduler;
}

// Test seam: disables the periodic queue refresh timer for tests that use AddonsScheduler.MainThread.
// The periodic timer uses Observable.Interval which requires a scheduler with timing support
// (e.g. DefaultScheduler). Test schedulers like CurrentThreadScheduler do not support timers.
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
internal static class AddonsTestHooks
{
    public static bool DisablePeriodicRefresh { get; set; }
}

public class AddonsOverviewViewModel : ViewModelBase, IDisposable
{
    private RangedObservableCollection<Addon> _modsSource = [];
    private readonly IDisposable _queueRefreshSubscription;
    private bool _disposed;
    private int _isQueueStateRefreshRunning;

    protected RangedObservableCollection<Addon> ModsSource
    {
        get => _modsSource;
        set
        {
            _modsSource = value;
            this.WhenAnyValue(x => x._modsSource.Count)
                .Throttle(TimeSpan.FromMilliseconds(100), AddonsScheduler.MainThread)
                .Subscribe(_ => FilterMods());
            FilterMods();
            _ = RefreshQueueStateAsync();
        }
    }

    private bool _isFiltered;
    [Reactive] public bool IsFiltering { get; set; }
    public virtual bool IsScanning => false;

    [Reactive] public RangedObservableCollection<Addon> DisplayedMods { get; set; } = [];
    [Reactive] public string ModsFilter { get; set; } = string.Empty;
    [Reactive] public bool BrowseMode { get; set; }
    [Reactive] public Addon? DataGridModsSelectedItem { get; set; }
    public bool IsAddonsDisplayed => !_isFiltered || DisplayedMods.Count > 0;

    public AsyncRelayCommand UpdateAllCommand { get; }
    public RelayCommand FilterModsCommand { get; }
    public RelayCommand RefreshModsCommand { get; }

    public AddonsOverviewViewModel(bool browseMode)
    {
        BrowseMode = browseMode;

        UpdateAllCommand = new AsyncRelayCommand
        (async _ => await UpdateAll()
        );
        FilterModsCommand = new RelayCommand
        (_ => FilterMods()
        );
        RefreshModsCommand = new RelayCommand
        (_ => RescanMods()
        );

        this.WhenAnyValue(x => x.DisplayedMods.Count)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsAddonsDisplayed)));

        _ = RefreshQueueStateAsync();

        if (AddonsTestHooks.DisablePeriodicRefresh)
        {
            _queueRefreshSubscription = Disposable.Empty;
        }
        else
        {
            _queueRefreshSubscription = Observable.Interval(TimeSpan.FromSeconds(1), AddonsScheduler.MainThread)
                .Subscribe(__ => { _ = RefreshQueueStateAsync(); });
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _queueRefreshSubscription.Dispose();
        _disposed = true;
    }

    private async Task UpdateAll()
    {
        Debug.WriteLine("Updating all outdated addons");

        var oldIsFiltering = IsFiltering;
        IsFiltering = true;

        try
        {
            foreach (var addon in ModsSource)
                if (addon.State is AddonState.Outdated or AddonState.InstallationError)
                {
                    var result = await addon.QueueUpdate(false);
                    if (!result.Accepted)
                        Debug.WriteLine($"Failed to queue update for {addon.Name}: {result.RejectionReason}");
                }
        }
        finally
        {
            IsFiltering = oldIsFiltering;
        }
    }

    protected async void FilterMods()
    {
        Debug.WriteLine("Filtering displayed addons");

        //if (IsFiltering) return;

        IsFiltering = true;

        await Task.Run(() =>
        {
            var filter = ModsFilter.Replace(" ", "");
            List<Addon> filteredAddons;
            if (!string.IsNullOrEmpty(filter))
                filteredAddons = ModsSource.Where(addon =>
                    addon.Name.Replace(" ", "").Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    addon.Categories.Any(category =>
                        category.Name.Replace(" ", "").Contains(filter, StringComparison.OrdinalIgnoreCase)) || // TODO move categories and authors to filters
                    addon.Authors.Any(author => author.Name.Replace(" ", "").Contains(filter, StringComparison.OrdinalIgnoreCase))
                ).ToList();
            else
                filteredAddons = [.. ModsSource];

            AddonsScheduler.MainThread.Schedule(() =>
            {
                DisplayedMods.Refresh(filteredAddons, false);
                IsFiltering = false;
            });
        });

        _isFiltered = true;
    }

    protected virtual void RescanMods()
    {
        Debug.WriteLine("Rescanning addons");
    }

    /// <summary>
    /// For testing: replaces the ModsSource without going through a protected property.
    /// </summary>
    internal void SetModsSourceForTesting(RangedObservableCollection<Addon> source)
    {
        ModsSource = source;
    }

    /// <summary>
    /// Returns the queue store used by <see cref="RefreshQueueStateAsync"/>.
    /// Virtual to allow tests to inject a different store.
    /// </summary>
    internal virtual IAddonOperationQueueStore GetQueueStore() => AddonServices.QueueStore;

    internal async Task RefreshQueueStateAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return;

        if (Interlocked.Exchange(ref _isQueueStateRefreshRunning, 1) == 1)
            return;

        try
        {
            var operations = await GetQueueStore().GetAllAsync(cancellationToken);
            var activeByAddon = operations
                .Where(o => o.Status is QueueOperationStatus.Pending or QueueOperationStatus.InProgress)
                .GroupBy(o => o.AddonCommonId)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .OrderBy(o => o.Status == QueueOperationStatus.InProgress ? 0 : 1)
                        .ThenBy(o => o.RequestTime)
                        .First());

            if (_disposed)
                return;

            AddonsScheduler.MainThread.Schedule(() =>
            {
                if (_disposed)
                    return;

                foreach (var addon in ModsSource)
                    if (activeByAddon.TryGetValue(addon.CommonAddonId, out var operation))
                        addon.SetQueuedOperation(operation);
                    else
                        addon.ClearQueuedOperation();
            });
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected during shutdown or refresh cancellation.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RefreshQueueState] Exception: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isQueueStateRefreshRunning, 0);
        }
    }
}
