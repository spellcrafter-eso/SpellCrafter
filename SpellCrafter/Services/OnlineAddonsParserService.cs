using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using SharpCompress.Archives;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

public partial class OnlineAddonsParserService : IArchiveDownloader
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(100)
    };

    private readonly HttpClient _httpClient;

    public OnlineAddonsParserService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    private const string BaseUrl = "https://www.esoui.com";
    private const string AddonsPath = "/addons.php";
    private const string DownloadsPath = "/downloads/getfile.php";

    private readonly ConcurrentBag<Task<List<(int, string)>>> _tasks = [];
    private readonly SemaphoreSlim _semaphore = new(20);

    public static event EventHandler? ScanningChanged;

    private static bool _isScanning;

    public static bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (_isScanning != value)
            {
                _isScanning = value;
                ScanningChanged?.Invoke(null, EventArgs.Empty);
            }
        }
    }

    public async Task<List<Addon>?> ParseAddonsAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsScanning) return null;
        IsScanning = true;

        var addonsMap = new Dictionary<int, Addon>();
        var categoryUrls = new List<string>
        {
            $"{BaseUrl}{AddonsPath}",
            $"{BaseUrl}/downloads/index.php?cid=39" // Category with subcategories //TODO maybe add support for subcategories
        };

#if DEBUG
        Stopwatch stopwatch = new();
        stopwatch.Start();
#endif
        foreach (var url in categoryUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var categoryInfos = await ParseCategoriesAsync(url);

            foreach (var categoryInfo in categoryInfos)
                _tasks.Add(ParseCategoryPageAsync(categoryInfo, true));
        }

        var results = await Task.WhenAll(_tasks);
        var addonsInfo = results.SelectMany(result => result).ToList();
        foreach (var (id, categoryName) in addonsInfo)
            if (addonsMap.TryGetValue(id, out var addon))
                addon.Categories.Add(new Category { Name = categoryName });
            else
                addonsMap.Add(id,
                    new Addon { UniqueId = id, Categories = [new Category { Name = categoryName }] });

        cancellationToken.ThrowIfCancellationRequested();

#if DEBUG
        stopwatch.Stop();
        Debug.WriteLine($"Site parsed in {stopwatch.Elapsed.Minutes}:{stopwatch.Elapsed.Seconds}");
#endif

        var tempFolder = Path.Combine(Path.GetTempPath(), "SpellCrafterParser");
        if (Directory.Exists(tempFolder))
            Directory.Delete(tempFolder, true);
        Directory.CreateDirectory(tempFolder);
        Debug.WriteLine($"Temp folder: {tempFolder}");

        try
        {
#if DEBUG
            stopwatch.Start();
#endif
            var addons = await ProcessAddonsAsync([.. addonsMap.Keys], tempFolder, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var addon in addons)
                addon.Categories = addonsMap[addon.UniqueId!.Value].Categories;
#if DEBUG
            stopwatch.Stop();
            Debug.WriteLine($"Addons parsed in {stopwatch.Elapsed.Minutes}:{stopwatch.Elapsed.Seconds}");
#endif
            return addons;
        }
        finally
        {
            if (Directory.Exists(tempFolder))
                Directory.Delete(tempFolder, true);

            IsScanning = false;
        }
    }

    private async Task<List<Addon>> ProcessAddonsAsync(
        IEnumerable<int> addonIds,
        string tempFolder,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tasks = addonIds.Select(id => ProcessAddonAsync(id, tempFolder, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.Where(addon => addon != null).Select(addon => addon!).ToList();
    }

    private async Task<Addon?> ProcessAddonAsync(
        int id,
        string tempFolder,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);

        const int maxRetryAttempts = 5;
        const int delayOnRetry = 1000;

        string? archivePath = null;
        for (var attempt = 0; attempt < maxRetryAttempts; ++attempt)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                archivePath = await DownloadAddonArchive(id, tempFolder, cancellationToken);
                break;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Error occurred while downloading addon with Id {id}: {ex.Message}. Attempt {attempt + 1} of {maxRetryAttempts}");
                if (attempt + 1 == maxRetryAttempts)
                    return null;
                await Task.Delay(delayOnRetry * (attempt + 1), cancellationToken);
            }
        }

        if (string.IsNullOrEmpty(archivePath)) return null;

        try
        {
            var tempManifestPath = ExtractAddonManifest(archivePath);
            if (string.IsNullOrEmpty(tempManifestPath)) return null;

            Addon? addon;
            try
            {
                addon = AddonManifestParser.ParseAddonManifest(tempManifestPath, true);
                addon.UniqueId = id;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
                return null;
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempManifestPath) && File.Exists(tempManifestPath))
                    File.Delete(tempManifestPath);
            }

            return addon;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error: {ex.Message}");
            return null;
        }
        finally
        {
            if (!string.IsNullOrEmpty(archivePath) && File.Exists(archivePath))
                File.Delete(archivePath);
            _semaphore.Release();
        }
    }

    private async Task<List<(string Url, string Name)>> ParseCategoriesAsync(string url)
    {
        var html = await _httpClient.GetStringAsync(url);
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html));

        return document.QuerySelectorAll("div.subtitle > a")
            .Select(node => (Url: node.GetAttribute("href"), Text: node.TextContent))
            .Where(item => !string.IsNullOrEmpty(item.Url))
            .ToList()!;
    }

    [GeneratedRegex(@"Page \d+ of (\d+)")]
    private static partial Regex PageRegex();

    [GeneratedRegex(@"/downloads/cat(\d+).html")]
    private static partial Regex CategoryRegex();

    private List<string> ParseCategoryPagination(IDocument document, string url)
    {
        var categoryPages = new List<string>();

        var pageControl = document.QuerySelector("td.vbmenu_control");
        if (pageControl == null) return categoryPages;

        var text = pageControl.TextContent;
        var matchPage = PageRegex().Match(text);
        if (!matchPage.Success) return categoryPages;

        var pageCount = int.Parse(matchPage.Groups[1].Value);
        var matchCategory = CategoryRegex().Match(url);
        if (!matchCategory.Success) return categoryPages;

        var categoryId = matchCategory.Groups[1].Value;
        for (var i = 2; i <= pageCount; ++i)
        {
            var pageUrl =
                $"{BaseUrl}/downloads/index.php?cid={categoryId}&sb=dec_date&so=desc&pt=f&page={i}";
            categoryPages.Add(pageUrl);
        }

        return categoryPages;
    }

    [GeneratedRegex(@"info(\d+)-")]
    private static partial Regex InfoIdRegex();

    private async Task<List<(int Id, string categoryName)>> ParseCategoryPageAsync((string Url, string Name) categoryInfo, bool parsePagination)
    {
        await _semaphore.WaitAsync();

        var html = await _httpClient.GetStringAsync(categoryInfo.Url);
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html));

        _semaphore.Release();

        if (parsePagination)
        {
            var paginationUrls = ParseCategoryPagination(document, categoryInfo.Url);
            foreach (var paginationUrl in paginationUrls) _tasks.Add(ParseCategoryPageAsync((paginationUrl, categoryInfo.Name), false));
        }

        var files = document.QuerySelectorAll("div.file > div.title > a");
        var regex = InfoIdRegex();

        return (from file in files
            select file.GetAttribute("href")
            into href
            select regex.Match(href)
            into match
            where match.Success
            select (int.Parse(match.Groups[1].Value), categoryInfo.Name)).ToList();
    }

    public async Task<string?> DownloadAddonArchive(
        int addonId,
        string tempFolder,
        CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}{DownloadsPath}?id={addonId}";
        using var response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Debug.WriteLine($"Failed to download the file for addon ID {addonId}");
            return null;
        }

        var contentDisposition = response.Content.Headers.ContentDisposition?.FileName?.Trim('\"');
        if (string.IsNullOrEmpty(contentDisposition))
        {
            Debug.WriteLine($"Could not get an addon with an ID {addonId}. This addon has probably been deleted");
            return null;
        }

        var supportedExtensions = new HashSet<string> { ".zip", ".rar", ".7z", ".gz", ".tar" };
        var fileExtension = Path.GetExtension(contentDisposition).ToLower();
        if (!supportedExtensions.Contains(fileExtension))
        {
            Debug.WriteLine($"Unsupported archive type or not an archive: {contentDisposition}");
            return null;
        }

        var safeFileName = $"{addonId}-{Guid.NewGuid():N}{fileExtension}";
        var archivePath = Path.Combine(tempFolder, safeFileName);

        try
        {
            await using var fs = new FileStream(
                archivePath, FileMode.CreateNew, FileAccess.Write,
                FileShare.Read, 4096, FileOptions.Asynchronous);

            await response.Content.CopyToAsync(fs, cancellationToken);
        }
        catch
        {
            if (File.Exists(archivePath))
                File.Delete(archivePath);

            throw;
        }

        return archivePath;
    }

    private string? ExtractAddonManifest(string archivePath)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);

            var manifestName =
                archive.Entries
                    .Where(e => !e.IsDirectory && e.Key != null && e.Key.Contains('/'))
                    .Select(e => e.Key?[..e.Key.IndexOf('/')])
                    .FirstOrDefault();
            if (string.IsNullOrEmpty(manifestName)) return null;

            var manifestFilePath = $"{manifestName}/{manifestName}.txt";

            var manifestEntry = archive.Entries.FirstOrDefault(e => e.Key != null &&
                                                                    e.Key.Equals(manifestFilePath, StringComparison.OrdinalIgnoreCase));
            if (manifestEntry?.Key == null) return null;

            var tempManifestPath = Path.Combine(Path.GetDirectoryName(archivePath)!, $"{manifestName}.txt");

            using var reader = manifestEntry.OpenEntryStream();
            using var fileStream = File.OpenWrite(tempManifestPath);
            Debug.WriteLine($"Extracting {manifestName} manifest from {Path.GetFileName(archivePath)}");

            reader.CopyTo(fileStream);

            return tempManifestPath;
        }
        catch (InvalidDataException ex)
        {
            Debug.WriteLine($"Archive {archivePath} is corrupted! {ex.Message}");
            return null;
        }
    }
}