using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Services;

namespace SpellCrafter.Tests.TestInfrastructure;

/// <summary>
/// Fake IArchiveDownloader that copies a pre-existing fixture archive to the requested tempFolder.
/// </summary>
public sealed class FakeArchiveDownloader : IArchiveDownloader
{
    private readonly string? _fixtureArchivePath;

    public FakeArchiveDownloader(string? fixtureArchivePath = null)
    {
        _fixtureArchivePath = fixtureArchivePath;
    }

    public Task<string?> DownloadAddonArchive(
        int addonId,
        string tempFolder,
        CancellationToken cancellationToken = default)
    {
        if (_fixtureArchivePath == null)
            return Task.FromResult<string?>(null);

        var destPath = Path.Combine(tempFolder, $"addon_{addonId}.zip");
        File.Copy(_fixtureArchivePath, destPath, true);
        return Task.FromResult<string?>(destPath);
    }
}