using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SpellCrafter.Enums;
using SpellCrafter.Models;

namespace SpellCrafter.Cli;

internal static class CliOutput
{
    public static void WriteError(TextWriter errorWriter, string message)
    {
        errorWriter.WriteLine($"Error: {message}");
    }

    public static void WriteWarning(TextWriter errorWriter, string message)
    {
        errorWriter.WriteLine($"Warning: {message}");
    }

    public static string FormatAddonTable(IReadOnlyList<Addon> addons)
    {
        if (addons.Count == 0)
            return "No addons found.";

        var lines = new List<string>();
        lines.Add(string.Empty);
        lines.Add($"{"Name",-30} {"State",-20} {"Version",-12} {"Latest",-12} Title");
        lines.Add(new string('-', 100));

        foreach (var addon in addons)
        {
            var state = addon.State == AddonState.NotInstalled ? "Not installed"
                : addon.State == AddonState.Installing ? "Installing"
                : addon.State == AddonState.Outdated ? "Outdated"
                : addon.State == AddonState.InstallationError ? "Error"
                : addon.State == AddonState.LatestVersion ? "Up to date"
                : addon.State.ToString();

            lines.Add($"{addon.Name,-30} {state,-20} {addon.DisplayedVersion,-12} {addon.DisplayedLatestVersion,-12} {addon.Title}");
        }

        lines.Add(string.Empty);
        lines.Add($"Total: {addons.Count}");

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatAddonDetail(Addon addon, string? addonPath)
    {
        var state = addon.State == AddonState.NotInstalled ? "Not installed"
            : addon.State == AddonState.Installing ? "Installing"
            : addon.State == AddonState.Outdated ? "Outdated"
            : addon.State == AddonState.InstallationError ? "Error"
            : addon.State == AddonState.LatestVersion ? "Up to date"
            : addon.State.ToString();

        var lines = new List<string>
        {
            $"Name:              {addon.Name}",
            $"Title:             {addon.Title}",
            $"State:             {state}",
            $"Version:           {addon.DisplayedVersion}",
            $"Latest version:    {addon.DisplayedLatestVersion}",
            $"Install method:    {addon.InstallationMethod}",
            $"Authors:           {string.Join(", ", addon.Authors.Select(a => a.Name))}",
            $"Categories:        {string.Join(", ", addon.Categories.Select(c => c.Name))}",
            $"Description:       {addon.Description}",
            $"Unique ID:         {addon.UniqueId?.ToString() ?? "N/A"}",
            $"Website:           {GetWebsiteUrl(addon.UniqueId)}"
        };

        if (addon.LocalDependencies.Count > 0)
            lines.Add($"Local deps:        {string.Join(", ", addon.LocalDependencies.Select(d => d.Name))}");

        if (addon.OnlineDependencies.Count > 0)
            lines.Add($"Online deps:       {string.Join(", ", addon.OnlineDependencies.Select(d => d.Name))}");

        if (addonPath != null)
            lines.Add($"Folder:            {addonPath}");

        return string.Join(Environment.NewLine, lines);
    }

    public static string GetWebsiteUrl(int? uniqueId)
    {
        return uniqueId.HasValue
            ? $"https://www.esoui.com/downloads/info{uniqueId.Value}"
            : "N/A";
    }

    public static string ToJson(IReadOnlyList<Addon> addons)
    {
        var dtos = addons.Select(ToDto).ToList();
        return JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = false });
    }

    public static string ToJson(Addon addon)
    {
        return JsonSerializer.Serialize(ToDto(addon), new JsonSerializerOptions { WriteIndented = false });
    }

    public static string ToJson(object obj)
    {
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = false });
    }

    private static AddonOutputDto ToDto(Addon addon)
    {
        return new AddonOutputDto
        {
            Name = addon.Name,
            Title = addon.Title,
            State = addon.State.ToString(),
            Version = addon.Version,
            DisplayedVersion = addon.DisplayedVersion,
            LatestVersion = addon.LatestVersion,
            DisplayedLatestVersion = addon.DisplayedLatestVersion,
            InstallationMethod = addon.InstallationMethod.ToString(),
            Authors = addon.Authors.Select(a => a.Name).ToArray(),
            Categories = addon.Categories.Select(c => c.Name).ToArray(),
            LocalDependencies = addon.LocalDependencies.Select(d => d.Name).ToArray(),
            OnlineDependencies = addon.OnlineDependencies.Select(d => d.Name).ToArray(),
            UniqueId = addon.UniqueId,
            WebsiteUrl = GetWebsiteUrl(addon.UniqueId)
        };
    }

    public static string FormatQueueOperationTable(IReadOnlyList<QueuedOperation> operations)
    {
        if (operations.Count == 0)
            return "Queue is empty.";

        var lines = new List<string>();
        lines.Add(string.Empty);
        lines.Add($"{"ID",-36} {"Addon",-25} {"Type",-12} {"Status",-12} {"Priority",-10} {"Requested",-9} Message");
        lines.Add(new string('-', 110));

        foreach (var op in operations.OrderBy(o => o.Priority).ThenBy(o => o.RequestTime))
        {
            var id = op.OperationId.ToString("N");
            var addon = op.AddonName ?? op.AddonCommonId.ToString();
            var type = op.OperationType;
            var status = FormatQueueStatus(op);
            var priority = op.Priority == QueuePriority.Low ? "Low" : "Normal";
            var time = op.RequestTime.ToString("HH:mm:ss");
            var message = FormatQueueMessage(op);

            lines.Add($"{id,-36} {addon,-25} {type,-12} {status,-12} {priority,-10} {time,-9} {message}");
        }

        lines.Add(string.Empty);
        lines.Add($"Total: {operations.Count}");

        return string.Join(Environment.NewLine, lines);
    }

    public static string ToJson(IReadOnlyList<QueuedOperation> operations)
    {
        var dtos = operations.Select(ToDto).ToList();
        return JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = false });
    }

    public static string ToJson(QueuedOperation op)
    {
        return JsonSerializer.Serialize(ToDto(op), new JsonSerializerOptions { WriteIndented = false });
    }

    private static QueuedOperationOutputDto ToDto(QueuedOperation op)
    {
        return new QueuedOperationOutputDto
        {
            OperationId = op.OperationId,
            AddonCommonId = op.AddonCommonId,
            AddonName = op.AddonName,
            OperationType = op.OperationType,
            Status = FormatQueueStatus(op),
            Priority = op.Priority == QueuePriority.Low ? "Low" : "Normal",
            RequestTime = op.RequestTime,
            StartTime = op.StartTime,
            CompletionTime = op.CompletionTime,
            ErrorMessage = op.ErrorMessage,
            ResultMessage = op.ResultMessage,
            CompletedAfterCancellation = op.CompletedAfterCancellation,
            CancelRequested = op.CancelRequested,
            CancelReason = op.CancelReason,
            CancelRequestedAtUtc = op.CancelRequestedAtUtc
        };
    }

    private static string FormatQueueStatus(QueuedOperation op)
    {
        return op.CancelRequested && op.Status == QueueOperationStatus.InProgress
            ? "Canceling"
            : op.Status.ToString();
    }

    private static string FormatQueueMessage(QueuedOperation op)
    {
        var message = op.ErrorMessage ?? op.ResultMessage;
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        const int maxLength = 80;
        return message.Length <= maxLength
            ? message
            : $"{message[..(maxLength - 1)]}…";
    }

    private sealed record AddonOutputDto
    {
        public string Name { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string DisplayedVersion { get; init; } = string.Empty;
        public string LatestVersion { get; init; } = string.Empty;
        public string DisplayedLatestVersion { get; init; } = string.Empty;
        public string InstallationMethod { get; init; } = string.Empty;
        public string[] Authors { get; init; } = [];
        public string[] Categories { get; init; } = [];
        public string[] LocalDependencies { get; init; } = [];
        public string[] OnlineDependencies { get; init; } = [];
        public int? UniqueId { get; init; }
        public string? WebsiteUrl { get; init; }
    }

    private sealed record QueuedOperationOutputDto
    {
        public Guid OperationId { get; init; }
        public int AddonCommonId { get; init; }
        public string AddonName { get; init; } = string.Empty;
        public string OperationType { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Priority { get; init; } = string.Empty;
        public DateTime RequestTime { get; init; }
        public DateTime? StartTime { get; init; }
        public DateTime? CompletionTime { get; init; }
        public string? ErrorMessage { get; init; }
        public string? ResultMessage { get; init; }
        public bool CompletedAfterCancellation { get; init; }
        public bool CancelRequested { get; init; }
        public string? CancelReason { get; init; }
        public DateTime? CancelRequestedAtUtc { get; init; }
    }
}
