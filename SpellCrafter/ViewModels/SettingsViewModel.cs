using Avalonia.Platform.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SpellCrafter.Services;
using System.Diagnostics;
using System.IO;
using System;
using Splat;
using SpellCrafter.Data;

namespace SpellCrafter.ViewModels;

public class SettingsViewModel : ViewModelBase, IRoutableViewModel
{
    private const string AddonsDirectoryName = "AddOns";
    public string? UrlPathSegment => "/settings";
    public IScreen HostScreen { get; }

    [Reactive] public string AddonsDirectory { get; set; }

    public RelayCommand BrowseAddonsFolderCommand { get; }
    public RelayCommand ApplyCommand { get; }

    public SettingsViewModel(IScreen? screen = null) : base()
    {
        HostScreen = screen ?? Locator.Current.GetService<IScreen>()!;

        AddonsDirectory = AppSettings.Instance.AddonsDirectory;

        BrowseAddonsFolderCommand = new RelayCommand(_ => BrowseAddonsFolder());
        ApplyCommand = new RelayCommand(
            _ => Apply(),
            _ => !string.IsNullOrEmpty(AddonsDirectory)
        );

        this.WhenAnyValue(x => x.AddonsDirectory)
            .Subscribe(_ => ApplyCommand.RaiseCanExecuteChanged());
    }

    private async void BrowseAddonsFolder()
    {
        var options = new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Addons folder"
        };

        var directoryPath = await StorageProviderService.OpenFolderPickerAsync(options);

        if (!CheckIsAddonDirectoryValid(directoryPath))
            return;

        AddonsDirectory = directoryPath;
    }

    private void Apply()
    {
        Debug.WriteLine("Apply!");
        if (AppSettings.Instance.AddonsDirectory != AddonsDirectory)
        {
            AppSettings.Instance.AddonsDirectory = AddonsDirectory;
            AppSettings.Instance.Save();

            var addons = LocalAddonsScannerService.ScanDirectory(AddonsDirectory);
            if (addons != null)
            {
                using var db = new EsoDataConnection();
                AddonDataManager.UpdateInstalledAddonsInfo(db, addons);
            }
        }
    }

    public static bool CheckIsAddonDirectoryValid(string? directoryPath)
    {
        return AddonsDirectoryValidator.IsValidAddonsDirectory(directoryPath);
    }
}