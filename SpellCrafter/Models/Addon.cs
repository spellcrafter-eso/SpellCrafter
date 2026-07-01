using System;
using Avalonia.Input.Platform;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SpellCrafter.Enums;
using SpellCrafter.Messages;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Windows.Input;
using SpellCrafter.Services;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SpellCrafter.Models;

public class Addon : ReactiveObject, ILocalAddon, IOnlineAddon, ICommonAddon
{
    private const string BaseAddonPageLink = "https://www.esoui.com/downloads/info";
    private static readonly SemaphoreSlim OperationLock = new(1, 1);
    private static bool _isOperationInProgress;

    public static bool IsOperationInProgress => Volatile.Read(ref _isOperationInProgress);

    public static event EventHandler? OperationStateChanged;

    private static void SetOperationInProgress(bool value)
    {
        if (Volatile.Read(ref _isOperationInProgress) == value)
            return;

        Volatile.Write(ref _isOperationInProgress, value);
        OperationStateChanged?.Invoke(null, EventArgs.Empty);
        RaiseKnownOperationCanExecuteChanged();
    }

    private static void RaiseKnownOperationCanExecuteChanged()
    {
        foreach (var addon in AddonDataManager.InstalledAddons
                     .Concat(AddonDataManager.OnlineAddons)
                     .Distinct())
            addon.RaiseOperationCommandCanExecuteChanged();
    }

    private void RaiseOperationCommandCanExecuteChanged()
    {
        InstallCommand.RaiseCanExecuteChanged();
        ReinstallCommand.RaiseCanExecuteChanged();
        UpdateCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
    }

    private static async Task<T> RunExclusiveAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await OperationLock.WaitAsync(cancellationToken);

        SetOperationInProgress(true);

        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            SetOperationInProgress(false);
            OperationLock.Release();
        }
    }

    public int CommonAddonId { get; set; } = -1;
    public int? LocalAddonId { get; set; }
    public int? OnlineAddonId { get; set; }
    [Reactive] public string Name { get; set; } = string.Empty;
    [Reactive] public string Title { get; set; } = string.Empty;
    [Reactive] public string Description { get; set; } = string.Empty;
    [Reactive] public AddonState State { get; set; } = AddonState.NotInstalled;
    [Reactive] public AddonInstallationMethod InstallationMethod { get; set; } = AddonInstallationMethod.Other;
    [Reactive] public string Downloads { get; set; } = "TODO downloads";
    [Reactive] public ObservableCollection<Author> Authors { get; set; } = [];
    IList<Author> ICommonAddon.Authors => Authors;
    [Reactive] public ObservableCollection<Category> Categories { get; set; } = [];
    IList<Category> ICommonAddon.Categories => Categories;
    [Reactive] public RangedObservableCollection<CommonAddon> LocalDependencies { get; set; } = [];
    [Reactive] public RangedObservableCollection<CommonAddon> OnlineDependencies { get; set; } = [];
    [Reactive] public int? UniqueId { get; set; }
    [Reactive] public string FileSize { get; set; } = "TODO archive size";
    [Reactive] public string Overview { get; set; } = "TODO";
    [Reactive] public string Version { get; set; } = string.Empty;
    [Reactive] public string DisplayedVersion { get; set; } = string.Empty;
    [Reactive] public string LatestVersion { get; set; } = string.Empty;
    [Reactive] public string DisplayedLatestVersion { get; set; } = string.Empty;

    public ICommand ViewModCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public AsyncRelayCommand ReinstallCommand { get; }
    public AsyncRelayCommand UpdateCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public ICommand ViewWebsiteCommand { get; }
    public ICommand CopyLinkCommand { get; }
    public ICommand BrowseFolderCommand { get; }

    private IAddonInstallationService? _addonInstallationService;

    public Addon()
    {
        ViewModCommand = new RelayCommand(_ => ViewMod());
        InstallCommand = new AsyncRelayCommand(
            async _ => await Install(),
            _ => !IsOperationInProgress && State == AddonState.NotInstalled
        );
        ReinstallCommand = new AsyncRelayCommand(
            async _ => await Reinstall(),
            _ => !IsOperationInProgress && State != AddonState.NotInstalled
        );
        UpdateCommand = new AsyncRelayCommand(
            async _ => await Update(),
            _ => !IsOperationInProgress && State is AddonState.Outdated or AddonState.InstallationError
        );
        DeleteCommand = new AsyncRelayCommand(
            async _ => await Delete(),
            _ => !IsOperationInProgress && State != AddonState.NotInstalled
        );
        ViewWebsiteCommand = new RelayCommand(_ => ViewWebsite());
        CopyLinkCommand = new RelayCommand(_ => CopyLink());
        BrowseFolderCommand = new RelayCommand(
            _ => BrowseFolder(),
            _ => State != AddonState.NotInstalled
        );

        this.WhenAnyValue(x => x.State)
            .Subscribe(_ => RaiseOperationCommandCanExecuteChanged());
    }

    public Addon(IAddonInstallationService addonInstallationService)
        : this()
    {
        AttachInstallationService(addonInstallationService);
    }

    internal void AttachInstallationService(IAddonInstallationService addonInstallationService)
    {
        _addonInstallationService = addonInstallationService
                                    ?? throw new ArgumentNullException(nameof(addonInstallationService));
    }

    private IAddonInstallationService AddonInstallationService =>
        _addonInstallationService
        ?? throw new InvalidOperationException(
            "Addon installation service has not been configured.");

    private void ViewMod()
    {
        Debug.WriteLine($"Opening addon {Name} page");

        MessageBus.Current.SendMessage(new ViewAddonMessage(this));
    }

    public async Task<InstallResult> Install(
        AddonInstallationMethod installationMethod = AddonInstallationMethod.SpellCrafter,
        bool recursive = true,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunExclusiveAsync(
            ct => AddonInstallationService.InstallAsync(this, installationMethod, recursive, progress, ct),
            cancellationToken);
    }

    public async Task<InstallResult> Reinstall(
        bool recursive = true,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunExclusiveAsync(
            ct => AddonInstallationService.ReinstallAsync(this, recursive, progress, ct),
            cancellationToken);
    }

    public async Task<InstallResult> Update(
        bool recursive = true,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (AddonVersionComparer.CompareVersions(Version, LatestVersion) >= 0)
            return InstallResult.Success();

        return await RunExclusiveAsync(
            ct => AddonInstallationService.UpdateAsync(this, recursive, progress, ct),
            cancellationToken);
    }

    public async Task<InstallResult> Delete(
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunExclusiveAsync(
            ct => AddonInstallationService.DeleteAsync(this, progress, ct),
            cancellationToken);
    }

    private void ViewWebsite()
    {
        Debug.WriteLine($"Opening addon {Name} website");

        if (UniqueId == null)
            return;

        var url = $"{BaseAddonPageLink}{UniqueId}";
        OpenUrl(url);
    }

    private async void CopyLink()
    {
        Debug.WriteLine($"Copying addon {Name} link");

        if (UniqueId == null)
            return;

        var url = $"{BaseAddonPageLink}{UniqueId}";

        var clipboard = ClipboardService.Get();
        if (clipboard != null)
            await clipboard.SetTextAsync(url);
    }

    private void BrowseFolder()
    {
        Debug.WriteLine("BrowseFolder!");

        var addonPath = Path.Combine(AppSettings.Instance.AddonsDirectory, Name);
        if (!Directory.Exists(addonPath))
            return;

        OpenUrl(addonPath);
    }

    // ---- Operation infrastructure ----

    public sealed record AddonOperationPaths(
        string OperationId,
        string AddonsRootDirectory,
        string OperationsRootDirectory,
        string RootDirectory,
        string DownloadDirectory,
        string StagingDirectory,
        string BackupDirectory,
        string TargetAddonDirectory,
        string StagedAddonDirectory,
        string BackupAddonDirectory);

    // ---- State helpers ----

    internal void MarkInstalled(AddonInstallationMethod installationMethod)
    {
        State = AddonState.LatestVersion;
        InstallationMethod = installationMethod;
        Version = LatestVersion;
        DisplayedVersion = DisplayedLatestVersion;
        LocalDependencies.Refresh(OnlineDependencies);
    }

    internal void RestoreMetadata(
        AddonState state,
        AddonInstallationMethod installationMethod,
        string version,
        string displayedVersion)
    {
        State = state;
        InstallationMethod = installationMethod;
        Version = version;
        DisplayedVersion = displayedVersion;
    }

    private void OpenUrl(string url)
    {
        try
        {
            // hack because of this: https://github.com/dotnet/corefx/issues/10361
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                url = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    public LocalAddon ToLocalAddon()
    {
        return new LocalAddon
        {
            Id = LocalAddonId,
            CommonAddonId = CommonAddonId,
            Version = Version,
            DisplayedVersion = DisplayedVersion,
            State = State,
            InstallationMethod = InstallationMethod
        };
    }

    public CommonAddon ToCommonAddon()
    {
        return new CommonAddon
        {
            Id = CommonAddonId,
            Name = Name,
            Title = Title,
            Description = Description,
            Authors = [..Authors],
            Categories = [..Categories]
        };
    }
}