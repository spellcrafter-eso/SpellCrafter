using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Windows.Input;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SpellCrafter.Messages;
using SpellCrafter.Models;
using SpellCrafter.Services;

namespace SpellCrafter.ViewModels;

public class MainWindowViewModel : ViewModelBase, IScreen, IActivatableViewModel
{
    private readonly RangedObservableCollection<IRoutableViewModel> _navigationHistory = [];
    private int _currentHistoryIndex = -1;
    public ViewModelActivator Activator { get; } = new();
    [Reactive] public RoutingState Router { get; set; } = new();
    [Reactive] public bool IsMyModsButtonChecked { get; set; }
    [Reactive] public bool IsBrowseButtonChecked { get; set; }
    [Reactive] public bool IsSettingsButtonChecked { get; set; }

    [Reactive] public RangedObservableCollection<OperationRecoveryNotice> RecoveryNotices { get; set; } = [];
    public bool HasRecoveryNotices => RecoveryNotices.Count > 0;

    public ICommand ShowInstalledAddonsCommand { get; }
    public ICommand ShowBrowseCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public RelayCommand GoBackCommand { get; }
    public RelayCommand GoForwardCommand { get; }

    public MainWindowViewModel()
    {
        RecoveryNotices = AddonOperationRecoveryService.Notices;
        RecoveryNotices.CollectionChanged += (_, _) =>
            this.RaisePropertyChanged(nameof(HasRecoveryNotices));

        var isAddonsDirectoryValid =
            SettingsViewModel.CheckIsAddonDirectoryValid(AppSettings.Instance.AddonsDirectory);

        if (!isAddonsDirectoryValid)
            AppSettings.Instance.AddonsDirectory = string.Empty;

        if (isAddonsDirectoryValid)
            IsMyModsButtonChecked = true;
        else
            IsSettingsButtonChecked = true;

        this.WhenActivated((CompositeDisposable _) =>
        {
            if (isAddonsDirectoryValid)
                NavigateToViewModel(new InstalledAddonsViewModel());
            else
                NavigateToViewModel(new SettingsViewModel());
        });

        MessageBus.Current.Listen<ViewAddonMessage>()
            .Subscribe(message =>
            {
                var addon = message.Addon;
                if (addon != null)
                    NavigateToViewModel(new AddonDetailsViewModel(addon));
            });

        ShowInstalledAddonsCommand = new RelayCommand
        (
            _ => NavigateToViewModel(new InstalledAddonsViewModel()),
            _ => Router.GetCurrentViewModel() is not InstalledAddonsViewModel
        );

        ShowBrowseCommand = new RelayCommand
        (
            _ => NavigateToViewModel(new BrowseViewModel()),
            _ => Router.GetCurrentViewModel() is not BrowseViewModel
        );

        ShowSettingsCommand = new RelayCommand
        (
            _ => NavigateToViewModel(new SettingsViewModel()),
            _ => Router.GetCurrentViewModel() is not SettingsViewModel
        );

        GoBackCommand = new RelayCommand
        (
            _ =>
            {
                if (_currentHistoryIndex <= 0) return;
                _currentHistoryIndex--;
                Router.Navigate.Execute(_navigationHistory[_currentHistoryIndex]);
                GoBackCommand?.RaiseCanExecuteChanged();
                GoForwardCommand?.RaiseCanExecuteChanged();
            },
            _ => _currentHistoryIndex > 0
        );

        GoForwardCommand = new RelayCommand
        (
            _ =>
            {
                if (_currentHistoryIndex >= _navigationHistory.Count - 1) return;
                _currentHistoryIndex++;
                Router.Navigate.Execute(_navigationHistory[_currentHistoryIndex]);
                GoBackCommand?.RaiseCanExecuteChanged();
                GoForwardCommand?.RaiseCanExecuteChanged();
            },
            _ => _currentHistoryIndex < _navigationHistory.Count - 1
        );
    }

    private void NavigateToViewModel(IRoutableViewModel viewModel)
    {
        Router.Navigate.Execute(viewModel);
        if (_navigationHistory.Count - 1 > _currentHistoryIndex)
            _navigationHistory.RemoveRange(_currentHistoryIndex + 1, _navigationHistory.Count - 1);

        _navigationHistory.Add(viewModel);
        _currentHistoryIndex++;
        GoBackCommand.RaiseCanExecuteChanged();
        GoForwardCommand.RaiseCanExecuteChanged();
    }
}