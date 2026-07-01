using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;
using SpellCrafter.Services;

namespace SpellCrafter.Cli;

internal static class CliCommandRunner
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter outputWriter,
        TextWriter errorWriter)
    {
        try
        {
            return await RunInternalAsync(args, outputWriter, errorWriter);
        }
        catch (OperationCanceledException)
        {
            errorWriter.WriteLine("Operation was canceled.");
            return CliExitCodes.Canceled;
        }
        catch (Exception ex)
        {
            errorWriter.WriteLine($"Unexpected error: {ex.Message}");
            if (ex.InnerException != null)
                errorWriter.WriteLine($"  Inner: {ex.InnerException.Message}");
            return CliExitCodes.SystemError;
        }
    }

    private static async Task<int> RunInternalAsync(
        string[] args,
        TextWriter outputWriter,
        TextWriter errorWriter)
    {
        var (globalOptions, commandArgs) = ParseGlobalOptions(args);

        if (globalOptions.ShowHelp)
        {
            PrintHelp(outputWriter);
            return CliExitCodes.Success;
        }

        // Apply --data-dir before any static initialization
        if (globalOptions.DataDir != null)
        {
            Directory.CreateDirectory(globalOptions.DataDir);
            Environment.CurrentDirectory = globalOptions.DataDir;
        }

        using var cts = new CancellationTokenSource();
        var progress = new ConsoleProgressReporter(errorWriter, globalOptions.NoProgress);

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            errorWriter.WriteLine("Cancellation requested...");
            cts.Cancel();
        };

        var cancellationToken = cts.Token;

        if (commandArgs.Count == 0)
        {
            PrintHelp(outputWriter);
            return CliExitCodes.Success;
        }

        var command = commandArgs[0].ToLowerInvariant();
        var subArgs = commandArgs.Skip(1).ToList();

        return command switch
        {
            "status" or "doctor" => RunStatus(subArgs, outputWriter, errorWriter, globalOptions),
            "config" or "cfg" or "settings" => RunConfig(subArgs, outputWriter, errorWriter, globalOptions),
            "list" or "ls" => RunList(subArgs, outputWriter, errorWriter, globalOptions),
            "search" or "find" => RunSearch(subArgs, outputWriter, errorWriter, globalOptions),
            "show" or "info" or "details" => RunShow(subArgs, outputWriter, errorWriter, globalOptions),
            "link" or "url" => RunLink(subArgs, outputWriter, errorWriter),
            "path" or "folder" or "addon-path" => RunPath(subArgs, outputWriter, errorWriter),
            "refresh" or "scan" or "sync" => await RunRefreshAsync(subArgs, outputWriter, errorWriter, progress, cancellationToken),
            "install" or "add" => await RunInstallAsync(subArgs, outputWriter, errorWriter, progress, cancellationToken, globalOptions),
            "reinstall" or "repair" => await RunReinstallAsync(subArgs, outputWriter, errorWriter, progress, cancellationToken, globalOptions),
            "update" or "upgrade" => await RunUpdateAsync(subArgs, outputWriter, errorWriter, progress, cancellationToken, globalOptions),
            "delete" or "remove" or "uninstall" or "rm" => await RunDeleteAsync(subArgs, outputWriter, errorWriter, progress, cancellationToken, globalOptions),
            _ => UnknownCommand(command, outputWriter, errorWriter)
        };
    }

    // ---- Global options parsing ----

    private static (GlobalOptions Options, List<string> Remaining) ParseGlobalOptions(string[] args)
    {
        var options = new GlobalOptions();
        var remaining = new List<string>();
        var i = 0;

        while (i < args.Length)
            switch (args[i].ToLowerInvariant())
            {
                case "--json":
                    options.Json = true;
                    i++;
                    break;
                case "--no-progress":
                    options.NoProgress = true;
                    i++;
                    break;
                case "--data-dir":
                    if (i + 1 >= args.Length)
                    {
                        remaining.Add(args[i]);
                        i++;
                    }
                    else
                    {
                        options.DataDir = args[i + 1];
                        i += 2;
                    }

                    break;
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    i++;
                    break;
                default:
                    remaining.Add(args[i]);
                    i++;
                    break;
            }

        return (options, remaining);
    }

    private sealed class GlobalOptions
    {
        public bool Json { get; set; }
        public bool NoProgress { get; set; }
        public string? DataDir { get; set; }
        public bool ShowHelp { get; set; }
    }

    // ---- Help ----

    private static void PrintHelp(TextWriter output)
    {
        output.WriteLine(@"SpellCrafter Mod Manager — CLI

Usage: spellcrafter <command> [options]

Global options:
  --json              Machine-readable JSON output to stdout
  --no-progress       Suppress progress output on stderr
  --data-dir <path>   Use custom data directory for settings/DB
  --help, -h          Show this help

Commands:
  status                    Show configuration and recovery status
  config get|set|list|detect|validate  Manage configuration
  list installed [options]  List installed addons
  list online [options]     List cached online addons
  search <query>            Search addons by name
  show <name>               Show addon details
  link <name>               Show ESOUI website URL
  path <name>               Show expected install folder path
  refresh installed         Rescan AddOns directory
  refresh online            Refresh online catalog from ESOUI
  refresh all               Both rescan and refresh
  install <name>            Install an addon
  reinstall <name>          Reinstall an addon
  update [<name>|--all]     Update addon(s)
  delete <name>             Delete/remove an addon

Run 'spellcrafter <command> --help' for command-specific options.");
    }

    // ---- Unknown command ----

    private static int UnknownCommand(string command, TextWriter output, TextWriter error)
    {
        error.WriteLine($"Unknown command: {command}");
        output.WriteLine("Run 'spellcrafter --help' for usage.");
        return CliExitCodes.UserError;
    }

    // ---- Status ----

    private static int RunStatus(
        List<string> args,
        TextWriter output,
        TextWriter error,
        GlobalOptions globalOptions)
    {
        // Access AddonDataManager triggers static initialization and recovery
        var installedCount = AddonDataManager.InstalledAddons.Count;
        var onlineCount = AddonDataManager.OnlineAddons.Count;
        var outdatedCount = AddonDataManager.InstalledAddons.Count(a => a.State == AddonState.Outdated);
        var errorCount = AddonDataManager.InstalledAddons.Count(a => a.State == AddonState.InstallationError);
        var configured = !string.IsNullOrWhiteSpace(AppSettings.Instance.AddonsDirectory);
        var dirValid = configured && AddonsDirectoryValidator.IsValidAddonsDirectory(AppSettings.Instance.AddonsDirectory);

        var notices = AddonOperationRecoveryService.Notices;

        if (globalOptions.Json)
        {
            var status = new
            {
                configured,
                addonsDirectory = AppSettings.Instance.AddonsDirectory,
                addonsDirectoryIsValid = dirValid,
                installedCount,
                onlineCount,
                outdatedCount,
                installationErrorCount = errorCount,
                operationInProgress = Addon.IsOperationInProgress,
                recoveryNoticeCount = notices.Count
            };
            output.WriteLine(CliOutput.ToJson(status));
        }
        else
        {
            output.WriteLine("Configuration:");
            output.WriteLine($"  AddOns directory: {AppSettings.Instance.AddonsDirectory}");
            output.WriteLine($"  Configured:       {configured}");
            output.WriteLine($"  Valid AddOns:     {dirValid}");
            output.WriteLine();
            output.WriteLine("Catalog:");
            output.WriteLine($"  Installed:        {installedCount}");
            output.WriteLine($"  Online cached:    {onlineCount}");
            output.WriteLine($"  Outdated:         {outdatedCount}");
            output.WriteLine($"  Errors:           {errorCount}");
            output.WriteLine();
            output.WriteLine("Recovery:");
            output.WriteLine($"  Notices:          {notices.Count}");

            foreach (var notice in notices)
            {
                output.WriteLine($"  - {notice.Message}");
                if (notice.BackupPath != null)
                    output.WriteLine($"    Backup: {notice.BackupPath}");
            }
        }

        return CliExitCodes.Success;
    }

    // ---- Config ----

    private static int RunConfig(
        List<string> args,
        TextWriter output,
        TextWriter error,
        GlobalOptions globalOptions)
    {
        var subcommand = args.Count > 0 ? args[0].ToLowerInvariant() : "list";

        switch (subcommand)
        {
            case "list":
                output.WriteLine($"addons-directory = {AppSettings.Instance.AddonsDirectory}");
                return CliExitCodes.Success;

            case "get":
                if (args.Count < 2)
                {
                    error.WriteLine("Usage: config get <key>");
                    return CliExitCodes.UserError;
                }

                if (args[1].ToLowerInvariant() == "addons-directory")
                {
                    var dir = AppSettings.Instance.AddonsDirectory;
                    if (string.IsNullOrWhiteSpace(dir))
                    {
                        error.WriteLine("AddOns directory is not configured.");
                        return CliExitCodes.UserError;
                    }

                    output.WriteLine(dir);
                    return CliExitCodes.Success;
                }

                error.WriteLine($"Unknown config key: {args[1]}");
                return CliExitCodes.UserError;

            case "set":
                if (args.Count < 3 || args[1].ToLowerInvariant() != "addons-directory")
                {
                    error.WriteLine("Usage: config set addons-directory <path>");
                    return CliExitCodes.UserError;
                }

                return SetAddonsDirectory(args[2], output, error, globalOptions);

            case "unset":
                AppSettings.Instance.AddonsDirectory = string.Empty;
                AppSettings.Instance.Save();
                output.WriteLine("AddOns directory cleared.");
                return CliExitCodes.Success;

            case "validate":
                var validationPath = AppSettings.Instance.AddonsDirectory;
                var validationError = AddonsDirectoryValidator.GetValidationError(validationPath);
                if (validationError != null)
                {
                    error.WriteLine(validationError);
                    return CliExitCodes.UserError;
                }

                output.WriteLine($"AddOns directory is valid: {validationPath}");
                return CliExitCodes.Success;

            case "detect":
            case "autodetect":
            case "detect-addons-directory":
                return RunDetectAddonsDirectory(args.Skip(1).ToList(), output, error, globalOptions);

            default:
                error.WriteLine($"Unknown config subcommand: {subcommand}");
                return CliExitCodes.UserError;
        }
    }

    private static int SetAddonsDirectory(
        string path,
        TextWriter output,
        TextWriter error,
        GlobalOptions globalOptions)
    {
        path = Path.GetFullPath(path);

        var validationError = AddonsDirectoryValidator.GetValidationError(path);
        if (validationError != null)
        {
            error.WriteLine(validationError);
            return CliExitCodes.UserError;
        }

        AppSettings.Instance.AddonsDirectory = path;
        AppSettings.Instance.Save();

        var addons = LocalAddonsScannerService.ScanDirectory(path);
        if (addons != null)
        {
            using var db = new EsoDataConnection();
            AddonDataManager.UpdateInstalledAddonsInfo(db, addons);
        }

        output.WriteLine($"AddOns directory set to: {path}");

        if (addons != null)
            output.WriteLine($"Scanned installed addons: {addons.Count}");

        return CliExitCodes.Success;
    }

    private static int RunDetectAddonsDirectory(
        List<string> args,
        TextWriter output,
        TextWriter error,
        GlobalOptions globalOptions)
    {
        var flags = ParseFlags(args);
        var shouldSet = flags.ContainsKey("set");

        var result = AddonsDirectoryDiscoveryService.Discover();

        if (globalOptions.Json)
        {
            var dto = new
            {
                candidates = result.Candidates.Select(c => new
                {
                    path = c.Path,
                    displayName = c.DisplayName,
                    sourceDescription = c.SourceDescription
                }).ToList(),
                warnings = result.Warnings
            };
            output.WriteLine(CliOutput.ToJson(dto));
        }
        else
        {
            foreach (var warning in result.Warnings)
                error.WriteLine($"Warning: {warning}");

            if (!result.HasCandidates)
            {
                error.WriteLine("No ESO AddOns folder was detected.");
                error.WriteLine("Use 'config set addons-directory <path>' to configure it manually.");
                return CliExitCodes.UserError;
            }

            output.WriteLine("Detected ESO AddOns folders:");
            for (var i = 0; i < result.Candidates.Count; i++)
            {
                var c = result.Candidates[i];
                output.WriteLine($"  {i + 1}. {c.DisplayName} — {c.Path}");
            }
        }

        if (!shouldSet)
            return CliExitCodes.Success;

        if (result.Candidates.Count == 0)
        {
            error.WriteLine("Cannot use --set: no ESO AddOns folder was detected.");
            return CliExitCodes.UserError;
        }

        if (result.Candidates.Count > 1)
        {
            error.WriteLine("Cannot use --set: multiple ESO AddOns folders were detected.");
            error.WriteLine("Use 'config set addons-directory \"<path>\"' to configure one manually.");
            return CliExitCodes.UserError;
        }

        return SetAddonsDirectory(result.Candidates[0].Path, output, error, globalOptions);
    }

    // ---- List ----

    private static int RunList(
        List<string> args,
        TextWriter output,
        TextWriter error,
        GlobalOptions globalOptions)
    {
        var subcommand = args.Count > 0 ? args[0].ToLowerInvariant() : "installed";

        switch (subcommand)
        {
            case "installed" or "installed-addons" or "mods":
            {
                var flags = ParseFlags(args.Skip(1).ToList());
                var addons = AddonDataManager.InstalledAddons.AsEnumerable();

                if (flags.ContainsKey("outdated"))
                    addons = addons.Where(a => a.State == AddonState.Outdated);
                else if (flags.ContainsKey("errors"))
                    addons = addons.Where(a => a.State == AddonState.InstallationError);

                var list = addons.ToList();

                if (globalOptions.Json)
                    output.WriteLine(CliOutput.ToJson(list));
                else
                    output.WriteLine(CliOutput.FormatAddonTable(list));

                return CliExitCodes.Success;
            }

            case "online" or "browse":
            {
                var flags = ParseFlags(args.Skip(1).ToList());
                var addons = AddonDataManager.OnlineAddons.AsEnumerable();

                if (flags.ContainsKey("not-installed"))
                {
                    var installedIds = new HashSet<int>(
                        AddonDataManager.InstalledAddons
                            .Where(a => a.CommonAddonId != 0)
                            .Select(a => a.CommonAddonId));
                    addons = addons.Where(a => !installedIds.Contains(a.CommonAddonId));
                }

                var list = addons.ToList();

                if (globalOptions.Json)
                    output.WriteLine(CliOutput.ToJson(list));
                else
                    output.WriteLine(CliOutput.FormatAddonTable(list));

                return CliExitCodes.Success;
            }

            default:
                error.WriteLine("Usage: list installed|online [options]");
                return CliExitCodes.UserError;
        }
    }

    // ---- Search ----

    private static int RunSearch(
        List<string> args,
        TextWriter output,
        TextWriter error,
        GlobalOptions globalOptions)
    {
        if (args.Count == 0 || args[0].StartsWith("--"))
        {
            error.WriteLine("Usage: search <query> [--installed] [--online]");
            return CliExitCodes.UserError;
        }

        var query = args[0];
        var flags = ParseFlags(args.Skip(1).ToList());
        var searchInstalled = flags.ContainsKey("installed") || !flags.ContainsKey("online");
        var searchOnline = flags.ContainsKey("online") || !flags.ContainsKey("installed");
        var limit = flags.TryGetValue("limit", out var limitStr) && int.TryParse(limitStr, out var l) ? l : 50;

        var results = new List<Addon>();

        if (searchInstalled)
            results.AddRange(AddonDataManager.InstalledAddons
                .Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || a.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit));

        if (searchOnline && results.Count < limit)
        {
            var remaining = limit - results.Count;
            results.AddRange(AddonDataManager.OnlineAddons
                .Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || a.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(remaining));
        }

        if (globalOptions.Json)
            output.WriteLine(CliOutput.ToJson(results));
        else
            output.WriteLine(CliOutput.FormatAddonTable(results));

        return CliExitCodes.Success;
    }

    // ---- Show ----

    private static int RunShow(
        List<string> args,
        TextWriter output,
        TextWriter error,
        GlobalOptions globalOptions)
    {
        if (args.Count == 0)
        {
            error.WriteLine("Usage: show <name> [--id <id>]");
            return CliExitCodes.UserError;
        }

        var name = args[0];
        var flags = ParseFlags(args.Skip(1).ToList());

        Addon? addon;

        if (flags.TryGetValue("id", out var idStr) && int.TryParse(idStr, out var id))
        {
            addon = AddonResolver.ResolveById(id);
        }
        else
        {
            if (flags.ContainsKey("installed"))
                addon = AddonResolver.ResolveInstalled(name);
            else if (flags.ContainsKey("online"))
                addon = AddonResolver.ResolveOnline(name);
            else
                addon = AddonResolver.ResolveAny(name);
        }

        if (addon == null)
        {
            var candidates = AddonResolver.FindCandidates(name);
            if (candidates.Count > 0)
            {
                error.WriteLine($"Addon '{name}' not found. Did you mean:");
                foreach (var c in candidates.Take(5))
                    error.WriteLine($"  {c.Name} ({(c.State == AddonState.NotInstalled ? "online" : "installed")})");
            }
            else
            {
                error.WriteLine($"Addon '{name}' not found.");
            }

            return CliExitCodes.UserError;
        }

        string? addonPath = null;
        if (!string.IsNullOrWhiteSpace(AppSettings.Instance.AddonsDirectory))
        {
            var candidatePath = Path.Combine(AppSettings.Instance.AddonsDirectory, addon.Name);
            if (Directory.Exists(candidatePath))
                addonPath = candidatePath;
        }

        if (globalOptions.Json)
            output.WriteLine(CliOutput.ToJson(addon));
        else
            output.WriteLine(CliOutput.FormatAddonDetail(addon, addonPath));

        return CliExitCodes.Success;
    }

    // ---- Link ----

    private static int RunLink(
        List<string> args,
        TextWriter output,
        TextWriter error)
    {
        if (args.Count == 0)
        {
            error.WriteLine("Usage: link <name> [--id <id>]");
            return CliExitCodes.UserError;
        }

        var flags = ParseFlags(args.Skip(1).ToList());
        Addon? addon;

        if (flags.TryGetValue("id", out var idStr) && int.TryParse(idStr, out var id))
            addon = AddonResolver.ResolveById(id);
        else
            addon = AddonResolver.ResolveAny(args[0]);

        if (addon == null || addon.UniqueId == null)
        {
            error.WriteLine("Addon not found or has no online ID.");
            return CliExitCodes.UserError;
        }

        output.WriteLine(CliOutput.GetWebsiteUrl(addon.UniqueId));
        return CliExitCodes.Success;
    }

    // ---- Path ----

    private static int RunPath(
        List<string> args,
        TextWriter output,
        TextWriter error)
    {
        if (args.Count == 0)
        {
            error.WriteLine("Usage: path <name>");
            return CliExitCodes.UserError;
        }

        var addonName = args[0];
        var addonsDir = AppSettings.Instance.AddonsDirectory;

        if (string.IsNullOrWhiteSpace(addonsDir))
        {
            error.WriteLine("AddOns directory is not configured.");
            return CliExitCodes.UserError;
        }

        var addonPath = Path.Combine(addonsDir, addonName);
        output.WriteLine(Path.GetFullPath(addonPath));
        return CliExitCodes.Success;
    }

    // ---- Refresh ----

    private static async Task<int> RunRefreshAsync(
        List<string> args,
        TextWriter output,
        TextWriter error,
        ConsoleProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var subcommand = args.Count > 0 ? args[0].ToLowerInvariant() : "all";

        switch (subcommand)
        {
            case "installed":
                return await RefreshInstalledAsync(output, error, cancellationToken);

            case "online":
                return await RefreshOnlineAsync(output, error, cancellationToken);

            case "all":
            {
                var result = await RefreshInstalledAsync(output, error, cancellationToken);
                if (result != CliExitCodes.Success)
                    return result;
                return await RefreshOnlineAsync(output, error, cancellationToken);
            }

            default:
                error.WriteLine("Usage: refresh installed|online|all");
                return CliExitCodes.UserError;
        }
    }

    private static async Task<int> RefreshInstalledAsync(
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var path = AppSettings.Instance.AddonsDirectory;

        if (string.IsNullOrWhiteSpace(path))
        {
            error.WriteLine("AddOns directory is not configured.");
            return CliExitCodes.UserError;
        }

        error.WriteLine("Scanning installed addons...");
        var addons = LocalAddonsScannerService.ScanDirectory(path);

        if (addons == null)
        {
            error.WriteLine("Scanning is already in progress.");
            return CliExitCodes.UserError;
        }

        using var db = new EsoDataConnection();
        AddonDataManager.UpdateInstalledAddonsInfo(db, addons);
        output.WriteLine($"Found {addons.Count} installed addons.");
        return CliExitCodes.Success;
    }

    private static async Task<int> RefreshOnlineAsync(
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        error.WriteLine("Refreshing online addon catalog from ESOUI...");
        error.WriteLine("This can take several minutes.");

        try
        {
            var parser = new OnlineAddonsParserService();
            var addons = await parser.ParseAddonsAsync(cancellationToken);

            if (addons == null)
            {
                error.WriteLine("Failed to parse online catalog.");
                return CliExitCodes.SystemError;
            }

            using var db = new EsoDataConnection();
            AddonDataManager.UpdateOnlineAddonsInfo(db, addons);
            output.WriteLine($"Cached {addons.Count} online addons.");
            return CliExitCodes.Success;
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("Online refresh was canceled.");
            return CliExitCodes.Canceled;
        }
    }

    // ---- Install ----

    private static async Task<int> RunInstallAsync(
        List<string> args,
        TextWriter output,
        TextWriter error,
        ConsoleProgressReporter progress,
        CancellationToken cancellationToken,
        GlobalOptions globalOptions)
    {
        if (args.Count == 0)
        {
            error.WriteLine("Usage: install <name> [--id <id>] [--recursive] [--no-recursive]");
            return CliExitCodes.UserError;
        }

        var flags = ParseFlags(args.Skip(1).ToList());
        Addon? addon;

        if (flags.TryGetValue("id", out var idStr) && int.TryParse(idStr, out var id))
            addon = AddonResolver.ResolveById(id);
        else
            addon = AddonResolver.ResolveOnline(args[0]);

        if (addon == null)
        {
            error.WriteLine($"Addon '{args[0]}' not found in online catalog.");
            return CliExitCodes.UserError;
        }

        if (addon.State != AddonState.NotInstalled)
        {
            if (addon.State == AddonState.LatestVersion)
            {
                output.WriteLine($"'{addon.Name}' is already installed at the latest version.");
                return CliExitCodes.Success;
            }

            error.WriteLine($"'{addon.Name}' is already installed but outdated. Use 'update' instead.");
            return CliExitCodes.UserError;
        }

        var recursive = flags.ContainsKey("no-recursive") ? false : true;

        error.WriteLine($"Installing '{addon.Name}'...");
        var result = await addon.Install(
            AddonInstallationMethod.SpellCrafter,
            recursive,
            progress,
            cancellationToken);

        return ReportResult(result, addon.Name, "install", output, error);
    }

    // ---- Reinstall ----

    private static async Task<int> RunReinstallAsync(
        List<string> args,
        TextWriter output,
        TextWriter error,
        ConsoleProgressReporter progress,
        CancellationToken cancellationToken,
        GlobalOptions globalOptions)
    {
        if (args.Count == 0)
        {
            error.WriteLine("Usage: reinstall <name> [--recursive] [--no-recursive]");
            return CliExitCodes.UserError;
        }

        var flags = ParseFlags(args.Skip(1).ToList());
        var addon = AddonResolver.ResolveInstalled(args[0]);

        if (addon == null)
        {
            error.WriteLine($"Addon '{args[0]}' is not installed.");
            return CliExitCodes.UserError;
        }

        var recursive = flags.ContainsKey("no-recursive") ? false : true;

        error.WriteLine($"Reinstalling '{addon.Name}'...");
        var result = await addon.Reinstall(recursive, progress, cancellationToken);

        return ReportResult(result, addon.Name, "reinstall", output, error);
    }

    // ---- Update ----

    private static async Task<int> RunUpdateAsync(
        List<string> args,
        TextWriter output,
        TextWriter error,
        ConsoleProgressReporter progress,
        CancellationToken cancellationToken,
        GlobalOptions globalOptions)
    {
        var flags = ParseFlags(args.Where(a => a.StartsWith("--")).ToList());
        var nonFlagArgs = args.Where(a => !a.StartsWith("--")).ToList();

        // Single addon update
        if (nonFlagArgs.Count > 0)
        {
            var addon = AddonResolver.ResolveInstalled(nonFlagArgs[0]);
            if (addon == null)
            {
                error.WriteLine($"Addon '{nonFlagArgs[0]}' is not installed.");
                return CliExitCodes.UserError;
            }

            if (addon.State == AddonState.LatestVersion)
            {
                output.WriteLine($"'{addon.Name}' is already up to date.");
                return CliExitCodes.Success;
            }

            var recursive = flags.ContainsKey("no-recursive") ? false : true;

            error.WriteLine($"Updating '{addon.Name}'...");
            var result = await addon.Update(recursive, progress, cancellationToken);

            return ReportResult(result, addon.Name, "update", output, error);
        }

        // Bulk update all
        return await RunBulkUpdateAsync(output, error, progress, cancellationToken);
    }

    private static async Task<int> RunBulkUpdateAsync(
        TextWriter output,
        TextWriter error,
        ConsoleProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var toUpdate = AddonDataManager.InstalledAddons
            .Where(a => a.State is AddonState.Outdated or AddonState.InstallationError)
            .ToList();

        if (toUpdate.Count == 0)
        {
            output.WriteLine("All addons are up to date.");
            return CliExitCodes.Success;
        }

        error.WriteLine($"Updating {toUpdate.Count} addon(s)...");

        var updated = 0;
        var failed = 0;
        var failedNames = new List<string>();

        foreach (var addon in toUpdate)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            error.WriteLine($"  Updating '{addon.Name}'...");
            var result = await addon.Update(false, progress, cancellationToken);

            if (result.Succeeded)
            {
                updated++;
            }
            else
            {
                failed++;
                failedNames.Add(addon.Name);
            }
        }

        output.WriteLine($"Updated: {updated}, Failed: {failed}, Skipped up-to-date: " +
                         $"{AddonDataManager.InstalledAddons.Count - toUpdate.Count}");

        if (failedNames.Count > 0)
            error.WriteLine($"Failed addons: {string.Join(", ", failedNames)}");

        return failed > 0 ? CliExitCodes.SystemError : CliExitCodes.Success;
    }

    // ---- Delete ----

    private static async Task<int> RunDeleteAsync(
        List<string> args,
        TextWriter output,
        TextWriter error,
        ConsoleProgressReporter progress,
        CancellationToken cancellationToken,
        GlobalOptions globalOptions)
    {
        if (args.Count == 0)
        {
            error.WriteLine("Usage: delete <name> [--yes]");
            return CliExitCodes.UserError;
        }

        var flags = ParseFlags(args.Skip(1).ToList());
        var addon = AddonResolver.ResolveInstalled(args[0]);

        if (addon == null)
        {
            output.WriteLine($"Addon '{args[0]}' is not installed.");
            return CliExitCodes.Success;
        }

        if (!flags.ContainsKey("yes") && !flags.ContainsKey("y"))
        {
            error.WriteLine($"Use --yes to confirm deletion of '{addon.Name}'.");
            return CliExitCodes.UserError;
        }

        error.WriteLine($"Deleting '{addon.Name}'...");
        var result = await addon.Delete(progress, cancellationToken);

        return ReportResult(result, addon.Name, "delete", output, error);
    }

    // ---- Helpers ----

    private static int ReportResult(
        InstallResult result,
        string addonName,
        string operation,
        TextWriter output,
        TextWriter error)
    {
        if (result.Succeeded)
        {
            if (result.CompletedAfterCancellation)
                error.WriteLine($"'{addonName}' {operation} completed after cancellation was requested.");
            else
                error.WriteLine($"'{addonName}' {operation} succeeded.");

            if (result.WarningMessage != null)
                error.WriteLine($"Warning: {result.WarningMessage}");

            return CliExitCodes.Success;
        }

        if (result.WasCanceled)
        {
            error.WriteLine($"'{addonName}' {operation} was canceled.");
            return CliExitCodes.Canceled;
        }

        error.WriteLine($"'{addonName}' {operation} failed: {result.ErrorMessage}");

        if (result.BackupPath != null)
            error.WriteLine($"Backup preserved at: {result.BackupPath}");

        return CliExitCodes.SystemError;
    }

    private static Dictionary<string, string> ParseFlags(List<string> args)
    {
        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;

        while (i < args.Count)
        {
            var arg = args[i];

            if (!arg.StartsWith("--"))
            {
                i++;
                continue;
            }

            var key = arg.TrimStart('-').ToLowerInvariant();

            if (i + 1 < args.Count && !args[i + 1].StartsWith("--"))
            {
                flags[key] = args[i + 1];
                i += 2;
            }
            else
            {
                flags[key] = "true";
                i++;
            }
        }

        return flags;
    }
}