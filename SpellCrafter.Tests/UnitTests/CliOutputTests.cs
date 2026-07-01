using System.Text.Json;
using SpellCrafter.Cli;
using SpellCrafter.Enums;

namespace SpellCrafter.Tests.UnitTests;

public sealed class CliOutputTests
{
    [Fact]
    public void FormatAddonTable_Empty_ReturnsNoAddonsMessage()
    {
        var result = CliOutput.FormatAddonTable([]);
        Assert.Contains("No addons found", result);
    }

    [Fact]
    public void FormatAddonTable_SingleAddon_ContainsFields()
    {
        var addon = new Addon
        {
            Name = "TestAddon",
            Title = "Test Addon Title",
            State = AddonState.LatestVersion,
            Version = "1.0",
            DisplayedVersion = "1.0.0",
            LatestVersion = "1.0",
            DisplayedLatestVersion = "1.0.0"
        };

        var result = CliOutput.FormatAddonTable([addon]);

        Assert.Contains("TestAddon", result);
        Assert.Contains("Up to date", result);
        Assert.Contains("Test Addon Title", result);
        Assert.Contains("Total:", result);
    }

    [Fact]
    public void FormatAddonTable_MultipleStates_ShowsCorrectly()
    {
        var addons = new List<Addon>
        {
            new() { Name = "A1", State = AddonState.NotInstalled, Version = "", DisplayedVersion = "" },
            new() { Name = "A2", State = AddonState.Outdated, Version = "1.0", DisplayedVersion = "1.0", LatestVersion = "2.0", DisplayedLatestVersion = "2.0" },
            new() { Name = "A3", State = AddonState.InstallationError, Version = "1.0", DisplayedVersion = "1.0" }
        };

        var result = CliOutput.FormatAddonTable(addons);

        Assert.Contains("Not installed", result);
        Assert.Contains("Outdated", result);
        Assert.Contains("Error", result);
    }

    [Fact]
    public void FormatAddonDetail_ContainsAllExpectedFields()
    {
        var addon = new Addon
        {
            Name = "DetailAddon",
            Title = "Detail Title",
            State = AddonState.LatestVersion,
            Version = "2.0",
            DisplayedVersion = "2.0.0",
            InstallationMethod = AddonInstallationMethod.SpellCrafter,
            UniqueId = 999,
            Description = "A test addon"
        };
        addon.Authors.Add(new Author { Name = "Author1" });

        var result = CliOutput.FormatAddonDetail(addon, "/path/to/addon");

        Assert.Contains("DetailAddon", result);
        Assert.Contains("Detail Title", result);
        Assert.Contains("Up to date", result);
        Assert.Contains("2.0.0", result);
        Assert.Contains("SpellCrafter", result);
        Assert.Contains("Author1", result);
        Assert.Contains("999", result);
        Assert.Contains("esoui.com", result);
        Assert.Contains("/path/to/addon", result);
    }

    [Fact]
    public void FormatAddonDetail_WithoutPath_DoesNotShowFolder()
    {
        var addon = new Addon { Name = "N", State = AddonState.NotInstalled };

        var result = CliOutput.FormatAddonDetail(addon, null);

        Assert.DoesNotContain("Folder:", result);
    }

    [Fact]
    public void ToJson_List_ValidJson()
    {
        var addons = new List<Addon>
        {
            new() { Name = "Addon1", State = AddonState.LatestVersion, Version = "1.0" },
            new() { Name = "Addon2", State = AddonState.NotInstalled }
        };

        var json = CliOutput.ToJson(addons);

        Assert.NotNull(json);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(2, parsed.GetArrayLength());
    }

    [Fact]
    public void ToJson_SingleAddon_ValidJson()
    {
        var addon = new Addon
        {
            Name = "SingleAddon",
            State = AddonState.Outdated,
            Version = "1.0",
            DisplayedVersion = "1.0.0",
            LatestVersion = "2.0",
            DisplayedLatestVersion = "2.0.0",
            InstallationMethod = AddonInstallationMethod.SpellCrafter,
            UniqueId = 555
        };
        addon.Authors.Add(new Author { Name = "Dev" });

        var json = CliOutput.ToJson(addon);

        Assert.NotNull(json);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);
        // Default JsonSerializerOptions (no PropertyNamingPolicy) uses PascalCase
        Assert.Equal("SingleAddon", parsed.GetProperty("Name").GetString());
        Assert.Equal("Outdated", parsed.GetProperty("State").GetString());
        Assert.Equal(555, parsed.GetProperty("UniqueId").GetInt32());
        Assert.Single(parsed.GetProperty("Authors").EnumerateArray());
    }

    [Fact]
    public void ToJson_Object_ValidJson()
    {
        var obj = new { Key = "value", Number = 42 };
        var json = CliOutput.ToJson(obj);

        Assert.NotNull(json);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("value", parsed.GetProperty("Key").GetString());
        Assert.Equal(42, parsed.GetProperty("Number").GetInt32());
    }

    [Fact]
    public void GetWebsiteUrl_WithUniqueId_ReturnsUrl()
    {
        var url = CliOutput.GetWebsiteUrl(12345);
        Assert.Equal("https://www.esoui.com/downloads/info12345", url);
    }

    [Fact]
    public void GetWebsiteUrl_WithoutUniqueId_ReturnsNA()
    {
        var url = CliOutput.GetWebsiteUrl(null);
        Assert.Equal("N/A", url);
    }

    [Fact]
    public void WriteError_WritesToTextWriter()
    {
        using var writer = new StringWriter();
        CliOutput.WriteError(writer, "something failed");
        Assert.Contains("Error: something failed", writer.ToString());
    }

    [Fact]
    public void WriteWarning_WritesToTextWriter()
    {
        using var writer = new StringWriter();
        CliOutput.WriteWarning(writer, "warning message");
        Assert.Contains("Warning: warning message", writer.ToString());
    }
}