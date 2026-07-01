using System;
using ReactiveUI.Fody.Helpers;
using SpellCrafter.Models;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using SpellCrafter.Enums;
using System.Reactive.Concurrency;
using System.Threading;

namespace SpellCrafter.ViewModels;

public class AddonsOverviewViewModel : ViewModelBase
{
    private RangedObservableCollection<Addon> _modsSource = [];

    protected RangedObservableCollection<Addon> ModsSource
    {
        get => _modsSource;
        set
        {
            _modsSource = value;
            this.WhenAnyValue(x => x._modsSource.Count)
                .Throttle(TimeSpan.FromMilliseconds(100), RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => FilterMods());
            FilterMods();
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
                    var result = await addon.Update(false);
                    if (!result.Succeeded)
                        Debug.WriteLine($"Failed to update {addon.Name}: {result.ErrorMessage}");
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

            RxSchedulers.MainThreadScheduler.Schedule(() =>
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
}