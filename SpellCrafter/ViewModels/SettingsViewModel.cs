using Avalonia.Platform.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SpellCrafter.Services;
using System;
using System.Linq;
using Splat;
using SpellCrafter.Data;

namespace SpellCrafter.ViewModels;

public class SettingsViewModel : ViewModelBase, IRoutableViewModel
{
    private const string AddonsDirectoryName = "AddOns";
    public string? UrlPathSegment => "/settings";
    public IScreen HostScreen { get; }

    [Reactive] public string AddonsDirectory { get; set; }

    [Reactive] public AddonsDirectoryCandidate? SelectedDetectedAddonsDirectory { get; set; }

    [Reactive] public string DetectionMessage { get; set; } = string.Empty;

    [Reactive] public string? ValidationMessage { get; set; }

    [Reactive] public string? SaveMessage { get; set; }

    public RangedObservableCollection<AddonsDirectoryCandidate> DetectedAddonsDirectories { get; } = [];

    public RangedObservableCollection<string> DetectionWarnings { get; } = [];

    [Reactive] public bool HasDetectedAddonsDirectories { get; set; }

    [Reactive] public bool HasDetectionWarnings { get; set; }

    public RelayCommand BrowseAddonsFolderCommand { get; }
    public RelayCommand DetectAddonsFolderCommand { get; }
    public RelayCommand ApplyCommand { get; }

    public SettingsViewModel(IScreen? screen = null) : base()
    {
        HostScreen = screen ?? Locator.Current.GetService<IScreen>()!;

        AddonsDirectory = AppSettings.Instance.AddonsDirectory;

        BrowseAddonsFolderCommand = new RelayCommand(_ => BrowseAddonsFolder());
        DetectAddonsFolderCommand = new RelayCommand(_ => DetectAddonsFolders());
        ApplyCommand = new RelayCommand(
            _ => Apply(),
            _ => !string.IsNullOrWhiteSpace(AddonsDirectory)
        );

        this.WhenAnyValue(x => x.AddonsDirectory)
            .Subscribe(_ =>
            {
                ClearStatusMessages();
                ApplyCommand.RaiseCanExecuteChanged();
            });

        this.WhenAnyValue(x => x.SelectedDetectedAddonsDirectory)
            .Subscribe(candidate =>
            {
                if (candidate != null)
                    AddonsDirectory = candidate.Path;
            });

        // Run discovery on load
        DetectAddonsFolders();
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

    private void DetectAddonsFolders()
    {
        var result = AddonsDirectoryDiscoveryService.Discover();

        DetectedAddonsDirectories.Refresh(result.Candidates.ToList());
        DetectionWarnings.Refresh(result.Warnings.ToList());

        HasDetectedAddonsDirectories = result.HasCandidates;
        HasDetectionWarnings = result.HasWarnings;

        if (!result.HasCandidates)
        {
            DetectionMessage = "No ESO AddOns folder was detected. Enter the path manually or use Browse.";
            return;
        }

        if (result.Candidates.Count == 1)
        {
            DetectionMessage = "Detected one ESO AddOns folder. Review it and click Apply to use it.";

            if (string.IsNullOrWhiteSpace(AddonsDirectory))
                SelectedDetectedAddonsDirectory = result.Candidates[0];

            return;
        }

        DetectionMessage = "Detected multiple ESO AddOns folders. Choose the one SpellCrafter should manage.";
    }

    private void Apply()
    {
        ValidationMessage = null;
        SaveMessage = null;

        string fullPath;
        try
        {
            fullPath = System.IO.Path.GetFullPath(AddonsDirectory);
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            ValidationMessage = $"Invalid AddOns directory path: {ex.Message}";
            return;
        }

        var validationError = AddonsDirectoryValidator.GetValidationError(fullPath);
        if (validationError != null)
        {
            ValidationMessage = validationError;
            return;
        }

        if (string.Equals(
                AppSettings.Instance.AddonsDirectory,
                fullPath,
                StringComparison.Ordinal))
        {
            SaveMessage = "AddOns directory is already configured.";
            return;
        }

        try
        {
            var addons = LocalAddonsScannerService.ScanDirectory(fullPath);

            if (addons != null)
            {
                using var db = new EsoDataConnection();
                AddonDataManager.UpdateInstalledAddonsInfo(db, addons);
            }

            AppSettings.Instance.AddonsDirectory = fullPath;
            AppSettings.Instance.Save();

            SaveMessage = addons == null
                ? "AddOns directory saved. Scanning is already in progress."
                : $"AddOns directory saved. Found {addons.Count} installed addon(s).";
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or System.IO.IOException or System.IO.InvalidDataException)
        {
            ValidationMessage =
                $"SpellCrafter could not scan the selected AddOns directory: {ex.Message}";
        }
    }

    public static bool CheckIsAddonDirectoryValid(string? directoryPath)
    {
        return AddonsDirectoryValidator.IsValidAddonsDirectory(directoryPath);
    }

    private void ClearStatusMessages()
    {
        ValidationMessage = null;
        SaveMessage = null;
    }
}