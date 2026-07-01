using System;
using SpellCrafter.Data;

namespace SpellCrafter.Services;

internal static class AddonServices
{
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

    internal static IEsoDataConnectionFactory DataConnectionFactory => DataConnectionFactoryLazy.Value;

    internal static IAddonOperationJournalStore JournalStore => JournalStoreLazy.Value;

    internal static IAddonDataManager DataManager => DataManagerLazy.Value;

    internal static IArchiveDownloader ArchiveDownloader => ArchiveDownloaderLazy.Value;

    internal static IAddonInstallationService InstallationService => InstallationServiceLazy.Value;
}