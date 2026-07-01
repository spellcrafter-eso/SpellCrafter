using System.Threading;
using System.Threading.Tasks;

namespace SpellCrafter.Services;

public interface IArchiveDownloader
{
    Task<string?> DownloadAddonArchive(
        int addonId,
        string tempFolder,
        CancellationToken cancellationToken = default);
}
