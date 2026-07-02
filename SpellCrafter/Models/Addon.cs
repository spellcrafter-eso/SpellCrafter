using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Input.Platform;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SpellCrafter.Enums;
using SpellCrafter.Messages;
using SpellCrafter.Services;

namespace SpellCrafter.Models;

public class Addon : ReactiveObject, ILocalAddon, IOnlineAddon, ICommonAddon
{
    private const string BaseAddonPageLink = "https://www.esoui.com/downloads/info";
    private const string QueueCancellationMessage = "Operation was canceled via queue management.";

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
    [Reactive] public Guid? QueuedOperationId { get; set; }
    [Reactive] public string QueuedOperationType { get; set; } = string.Empty;
    [Reactive] public QueueOperationStatus? QueuedOperationStatus { get; set; }
    [Reactive] public string QueuedOperationDisplayText { get; set; } = string.Empty;
    [Reactive] public bool QueuedOperationCancelRequested { get; set; }

    public bool HasQueuedOperation =>
        QueuedOperationStatus is QueueOperationStatus.Pending or QueueOperationStatus.InProgress;

    public bool CanCancelQueuedOperation =>
        QueuedOperationId.HasValue &&
        QueuedOperationStatus is QueueOperationStatus.Pending or QueueOperationStatus.InProgress &&
        !QueuedOperationCancelRequested;

    public ICommand ViewModCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public AsyncRelayCommand ReinstallCommand { get; }
    public AsyncRelayCommand UpdateCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand CancelQueuedOperationCommand { get; }
    public ICommand ViewWebsiteCommand { get; }
    public ICommand CopyLinkCommand { get; }
    public ICommand BrowseFolderCommand { get; }

    private IAddonInstallationService? _addonInstallationService;
    private IAddonOperationSubmissionService? _addonOperationSubmissionService;

    public Addon()
    {
        ViewModCommand = new RelayCommand(_ => ViewMod());
        InstallCommand = new AsyncRelayCommand(
            async _ => await QueueInstall(),
            _ => State == AddonState.NotInstalled && !HasQueuedOperation
        );
        ReinstallCommand = new AsyncRelayCommand(
            async _ => await QueueReinstall(),
            _ => State != AddonState.NotInstalled && !HasQueuedOperation
        );
        UpdateCommand = new AsyncRelayCommand(
            async _ => await QueueUpdate(),
            _ => State is AddonState.Outdated or AddonState.InstallationError && !HasQueuedOperation
        );
        DeleteCommand = new AsyncRelayCommand(
            async _ => await QueueDelete(),
            _ => State != AddonState.NotInstalled && !HasQueuedOperation
        );
        CancelQueuedOperationCommand = new AsyncRelayCommand(
            async _ => await CancelQueuedOperation(),
            _ => CanCancelQueuedOperation
        );
        ViewWebsiteCommand = new RelayCommand(_ => ViewWebsite());
        CopyLinkCommand = new RelayCommand(_ => CopyLink());
        BrowseFolderCommand = new RelayCommand(
            _ => BrowseFolder(),
            _ => State != AddonState.NotInstalled
        );

        this.WhenAnyValue(x => x.State)
            .Subscribe(_ => RefreshOperationCommands());

        this.WhenAnyValue(x => x.QueuedOperationStatus)
            .Subscribe(_ => RefreshOperationCommands());

        this.WhenAnyValue(x => x.QueuedOperationCancelRequested)
            .Subscribe(_ => RefreshOperationCommands());
    }

    public Addon(IAddonInstallationService addonInstallationService)
        : this()
    {
        AttachInstallationService(addonInstallationService);
    }

    public Addon(
        IAddonInstallationService addonInstallationService,
        IAddonOperationSubmissionService addonOperationSubmissionService)
        : this(addonInstallationService)
    {
        AttachOperationSubmissionService(addonOperationSubmissionService);
    }

    internal void AttachInstallationService(IAddonInstallationService addonInstallationService)
    {
        _addonInstallationService = addonInstallationService
                                    ?? throw new ArgumentNullException(nameof(addonInstallationService));

        if (addonInstallationService is IAddonOperationSubmissionService submissionService)
            _addonOperationSubmissionService ??= submissionService;
    }

    internal void AttachOperationSubmissionService(IAddonOperationSubmissionService addonOperationSubmissionService)
    {
        _addonOperationSubmissionService = addonOperationSubmissionService
                                           ?? throw new ArgumentNullException(nameof(addonOperationSubmissionService));
    }

    private IAddonInstallationService AddonInstallationService =>
        _addonInstallationService
        ?? throw new InvalidOperationException(
            "Addon installation service has not been configured.");

    private IAddonOperationSubmissionService AddonOperationSubmissionService =>
        _addonOperationSubmissionService
        ?? throw new InvalidOperationException(
            "Addon operation submission service has not been configured.");

    private void ViewMod()
    {
        Debug.WriteLine($"Opening addon {Name} page");

        MessageBus.Current.SendMessage(new ViewAddonMessage(this));
    }

    public Task<InstallResult> Install(
        AddonInstallationMethod installationMethod = AddonInstallationMethod.SpellCrafter,
        bool recursive = true,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return AddonInstallationService.InstallAsync(this, installationMethod, recursive, progress, cancellationToken);
    }

    public async Task<QueuedOperationSubmissionResult> QueueInstall(
        AddonInstallationMethod installationMethod = AddonInstallationMethod.SpellCrafter,
        bool recursive = true,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await AddonOperationSubmissionService.SubmitInstallAsync(
            this, installationMethod, recursive, progress, cancellationToken);

        ApplySubmissionResult(result);
        return result;
    }

    public Task<InstallResult> Reinstall(
        bool recursive = true,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return AddonInstallationService.ReinstallAsync(this, recursive, progress, cancellationToken);
    }

    public async Task<QueuedOperationSubmissionResult> QueueReinstall(
        bool recursive = true,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await AddonOperationSubmissionService.SubmitReinstallAsync(
            this, recursive, progress, cancellationToken);

        ApplySubmissionResult(result);
        return result;
    }

    public Task<InstallResult> Update(
        bool recursive = true,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (AddonVersionComparer.CompareVersions(Version, LatestVersion) >= 0)
            return Task.FromResult(InstallResult.Success());

        return AddonInstallationService.UpdateAsync(this, recursive, progress, cancellationToken);
    }

    public async Task<QueuedOperationSubmissionResult> QueueUpdate(
        bool recursive = true,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (AddonVersionComparer.CompareVersions(Version, LatestVersion) >= 0)
            return QueuedOperationSubmissionResult.Rejected("Addon is already up to date.");

        var result = await AddonOperationSubmissionService.SubmitUpdateAsync(
            this, recursive, progress, cancellationToken);

        ApplySubmissionResult(result);
        return result;
    }

    public Task<InstallResult> Delete(
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return AddonInstallationService.DeleteAsync(this, progress, cancellationToken);
    }

    public async Task<QueuedOperationSubmissionResult> QueueDelete(
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await AddonOperationSubmissionService.SubmitDeleteAsync(
            this, progress, cancellationToken);

        ApplySubmissionResult(result);
        return result;
    }

    private void ApplySubmissionResult(QueuedOperationSubmissionResult result)
    {
        if (result.Operation == null)
            return;

        SetQueuedOperation(result.Operation);
    }

    internal void SetQueuedOperation(QueuedOperation operation)
    {
        QueuedOperationId = operation.OperationId;
        QueuedOperationType = operation.OperationType;
        QueuedOperationStatus = operation.Status;
        QueuedOperationCancelRequested = operation.CancelRequested;
        QueuedOperationDisplayText = FormatQueuedOperationDisplayText(operation);
    }

    internal void SetQueuedOperation(QueuedOperationReceipt operation)
    {
        QueuedOperationId = operation.OperationId;
        QueuedOperationType = operation.OperationType;
        QueuedOperationStatus = operation.Status;
        QueuedOperationCancelRequested = operation.CancelRequested;
        QueuedOperationDisplayText = FormatQueuedOperationDisplayText(operation.OperationType, operation.Status, operation.CancelRequested);
    }

    internal void ClearQueuedOperation()
    {
        QueuedOperationId = null;
        QueuedOperationType = string.Empty;
        QueuedOperationStatus = null;
        QueuedOperationCancelRequested = false;
        QueuedOperationDisplayText = string.Empty;
    }

    private async Task CancelQueuedOperation()
    {
        if (QueuedOperationId == null)
            return;

        var requested = await AddonServices.QueueStore.RequestCancellationAsync(
            QueuedOperationId.Value, QueueCancellationMessage);

        if (!requested)
            return;

        if (QueuedOperationStatus == QueueOperationStatus.Pending)
        {
            ClearQueuedOperation();
            return;
        }

        if (QueuedOperationStatus == QueueOperationStatus.InProgress)
        {
            QueuedOperationCancelRequested = true;
            QueuedOperationDisplayText = FormatQueuedOperationDisplayText(
                QueuedOperationType,
                QueueOperationStatus.InProgress,
                true);
        }
    }

    private void RefreshOperationCommands()
    {
        InstallCommand.RaiseCanExecuteChanged();
        ReinstallCommand.RaiseCanExecuteChanged();
        UpdateCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
        CancelQueuedOperationCommand.RaiseCanExecuteChanged();
        this.RaisePropertyChanged(nameof(HasQueuedOperation));
        this.RaisePropertyChanged(nameof(CanCancelQueuedOperation));
    }

    private static string FormatQueuedOperationDisplayText(QueuedOperation operation)
    {
        return FormatQueuedOperationDisplayText(
            operation.OperationType,
            operation.Status,
            operation.CancelRequested);
    }

    private static string FormatQueuedOperationDisplayText(
        string operationType,
        QueueOperationStatus status,
        bool cancelRequested)
    {
        var operationName = operationType switch
        {
            AddonOperationType.Install => "install",
            AddonOperationType.Update => "update",
            AddonOperationType.Reinstall => "reinstall",
            AddonOperationType.Delete => "delete",
            AddonOperationType.CleanupOrphans => "orphan cleanup",
            _ => operationType
        };

        if (cancelRequested && status == QueueOperationStatus.InProgress)
            return $"Cancel requested for {operationName}";

        return status switch
        {
            QueueOperationStatus.Pending => $"Pending {operationName}",
            QueueOperationStatus.InProgress => operationType switch
            {
                AddonOperationType.Install => "Installing",
                AddonOperationType.Update => "Updating",
                AddonOperationType.Reinstall => "Reinstalling",
                AddonOperationType.Delete => "Deleting",
                AddonOperationType.CleanupOrphans => "Cleaning up orphans",
                _ => "In progress"
            },
            _ => $"{status} {operationName}"
        };
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
            Authors = [.. Authors],
            Categories = [.. Categories]
        };
    }
}
