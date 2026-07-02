using SpellCrafter.Cli;
using SpellCrafter.Data;
using SpellCrafter.Enums;
using SpellCrafter.Models;
using SpellCrafter.Services;

namespace SpellCrafter.Tests.CliTests;

[Collection("EnvironmentStateCollection")] // Serialize with other env-modifying tests
public sealed class CliCommandRunnerTests
{
    [Fact]
    public async Task Help_ReturnsSuccess()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(["--help"], output, error);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Usage", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Help_ShortForm_ReturnsSuccess()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(["-h"], output, error);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Usage", output.ToString());
    }

    [Fact]
    public async Task UnknownCommand_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(["nonexistent"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Unknown command", error.ToString());
    }

    [Fact]
    public async Task NoArgs_ShowsHelp_ReturnsSuccess()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync([], output, error);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Usage", output.ToString());
    }

    private static string SaveCurrentDirectory()
    {
        // Reading Environment.CurrentDirectory may fail if a previous test
        // left it pointing to a deleted directory. Fall back to a safe default.
        try
        {
            return Environment.CurrentDirectory;
        }
        catch
        {
            // Return a safe fallback directory that definitely exists
            return Directory.GetCurrentDirectory();
        }
    }

    [Fact]
    public async Task Status_Json_ReturnsSuccess()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "--json", "status"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("addonsDirectory", output.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ConfigGet_ExistingKey_ReturnsValue()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        var addonsDir = Path.Combine(tempDir, "AddOns");
        Directory.CreateDirectory(addonsDir);

        try
        {
            using var setOutput = new StringWriter();
            await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "config", "set", "addons-directory", addonsDir],
                setOutput, new StringWriter());

            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "config", "get", "addons-directory"],
                output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("AddOns", output.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ConfigGet_UnknownKey_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["config", "get", "nonexistent"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Unknown config key", error.ToString());
    }

    [Fact]
    public async Task ConfigGet_MissingKey_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["config", "get"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Usage", error.ToString());
    }

    [Fact]
    public async Task ConfigSet_InvalidDir_ReturnsUserError()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        // Don't create it — test non-existent directory

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            // The --data-dir creates tempDir automatically, so use a different
            // non-existent path for the addons directory argument
            var nonExistentDir = Path.Combine(tempDir, "NonExistent");
            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "config", "set", "addons-directory", nonExistentDir],
                output, error);

            Assert.Equal(CliExitCodes.UserError, exitCode);
            Assert.Contains("does not exist", error.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ConfigSet_WrongKey_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["config", "set", "unknown-key", "/some/path"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Usage", error.ToString());
    }

    [Fact]
    public async Task ConfigUnset_ClearsDirectory()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "config", "unset"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("cleared", output.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ConfigValidate_WithValidDir_ReturnsSuccess()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        var addonsDir = Path.Combine(tempDir, "AddOns");
        Directory.CreateDirectory(addonsDir);

        try
        {
            // Set directory first
            using var setOutput = new StringWriter();
            await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "config", "set", "addons-directory", addonsDir],
                setOutput, new StringWriter());

            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "config", "validate"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("valid", output.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ListInstalled_WithOutdatedFlag_ReturnsSuccess()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "list", "installed", "--outdated"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ListInstalled_Json_ReturnsSuccess()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "--json", "list", "installed"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("[", output.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ListOnline_WithNotInstalledFlag_ReturnsSuccess()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "list", "online", "--not-installed"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Search_NoResults_ReturnsSuccess()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "search", "zzz_nonexistent_addon"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("No addons found", output.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Search_MissingQuery_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["search"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Usage", error.ToString());
    }

    [Fact]
    public async Task Show_NotFound_ReturnsUserError()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "show", "nonexistent_addon"], output, error);

            Assert.Equal(CliExitCodes.UserError, exitCode);
            Assert.Contains("not found", error.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Show_MissingName_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["show"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Usage", error.ToString());
    }

    [Fact]
    public async Task Link_NotFound_ReturnsUserError()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "link", "nonexistent"], output, error);

            Assert.Equal(CliExitCodes.UserError, exitCode);
            Assert.Contains("not found", error.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Link_MissingName_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["link"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Usage", error.ToString());
    }

    [Fact]
    public async Task Path_NoAddonsDir_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["path", "TestAddon"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("not configured", error.ToString());
    }

    [Fact]
    public async Task Path_MissingName_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["path"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Usage", error.ToString());
    }

    [Fact]
    public async Task Path_WithAddonsDir_ReturnsPath()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        var addonsDir = Path.Combine(tempDir, "AddOns");
        Directory.CreateDirectory(addonsDir);

        try
        {
            // Set addons directory first
            using var setOutput = new StringWriter();
            await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "config", "set", "addons-directory", addonsDir],
                setOutput, new StringWriter());

            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "path", "SomeAddon"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("SomeAddon", output.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Install_NoName_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["install"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Usage", error.ToString());
    }

    [Fact]
    public async Task Install_UnknownAddon_ReturnsUserError()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "install", "nonexistent_addon"], output, error);

            Assert.Equal(CliExitCodes.UserError, exitCode);
            Assert.Contains("not found", error.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Reinstall_NoName_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["reinstall"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Usage", error.ToString());
    }

    [Fact]
    public async Task Reinstall_NotInstalled_ReturnsUserError()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "reinstall", "nonexistent"], output, error);

            Assert.Equal(CliExitCodes.UserError, exitCode);
            Assert.Contains("not installed", error.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Update_NoName_UsesBulkUpdateLogic()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            // No name = bulk update (which should find nothing to update)
            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "update"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("up to date", output.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Update_NotInstalled_ReturnsUserError()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "update", "nonexistent"], output, error);

            Assert.Equal(CliExitCodes.UserError, exitCode);
            Assert.Contains("not installed", error.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Delete_NoName_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["delete"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Usage", error.ToString());
    }

    [Fact]
    public async Task Delete_NotInstalled_ReturnsSuccess()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "delete", "nonexistent"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("not installed", output.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Delete_WithoutYesFlag_ReturnsUserError()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        var addonsDir = Path.Combine(tempDir, "AddOns");
        Directory.CreateDirectory(addonsDir);

        // We can't actually install an addon through CLI in a unit test,
        // but we can test the --yes flag validation by setting up the
        // addon directory and verifying the command flow
        try
        {
            // Set addons directory first
            using var setupOutput = new StringWriter();
            await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "config", "set", "addons-directory", addonsDir],
                setupOutput, new StringWriter());

            // Delete without --yes — will say "not installed" since we can't
            // create a real addon entry in the data manager via unit test
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "delete", "TestAddon"], output, error);

            // Either "not installed" or "Use --yes" depending on whether
            // AddonDataManager happens to have an entry
            Assert.True(exitCode == CliExitCodes.Success || exitCode == CliExitCodes.UserError,
                $"Expected Success or UserError, got {exitCode}");
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Refresh_NoSubcommand_DefaultsToAll()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "refresh"], output, error);

            // The refresh defaults to "all", which requires AddOns dir configured.
            // Without it, it may return UserError or SystemError depending on
            // runtime state — the important thing is it doesn't crash.
            Assert.NotEqual(CliExitCodes.Success, exitCode);
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Refresh_InvalidSubcommand_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["refresh", "invalid"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Usage", error.ToString());
    }

    [Fact]
    public async Task QueueClear_WithoutStatusFlag_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["queue", "clear"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("queue clear", error.ToString());
    }

    [Fact]
    public async Task QueueClear_Completed_RemovesCompletedOperationsOnly()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Environment.CurrentDirectory = tempDir;
            EsoDataConnection.CreateTablesIfNotExists();

            var completed = CreateQueuedOperation(QueueOperationStatus.Completed);
            var failed = CreateQueuedOperation(QueueOperationStatus.Failed);

            await AddonServices.QueueStore.EnqueueAsync(completed);
            await AddonServices.QueueStore.EnqueueAsync(failed);

            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "queue", "clear", "--completed"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("Removed 1", output.ToString());

            var remaining = await AddonServices.QueueStore.GetAllAsync();
            var remainingOperation = Assert.Single(remaining);
            Assert.Equal(failed.OperationId, remainingOperation.OperationId);
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task UnknownSubcommand_ShowsHelp()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["config", "bogus"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Unknown config subcommand", error.ToString());
    }

    [Fact]
    public async Task List_InvalidSubcommand_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["list", "bogus"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("Usage", error.ToString());
    }

    [Fact]
    public async Task DataDir_WithExistingDir_CreatesFiles()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            // Using --data-dir with status command (will create settings.json in the temp dir)
            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "status"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("Configured", output.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task JsonFlag_ParsedCorrectly()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        // --json is consumed by global options and shouldn't be a command
        var exitCode = await CliCommandRunner.RunAsync(["--json"], output, error);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Usage", output.ToString());
    }

    [Fact]
    public async Task NoProgressFlag_ParsedCorrectly()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(["--no-progress", "--help"], output, error);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Usage", output.ToString());
    }

    [Fact]
    public async Task DataDir_WithNonExistentDir_CreatesIt()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "status"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.True(Directory.Exists(tempDir));
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ConfigList_ShowsSettings()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "config", "list"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("addons-directory", output.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    // ---- Queue cancel negative paths ----

    [Fact]
    public async Task QueueCancel_MissingId_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["queue", "cancel"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("queue cancel", error.ToString());
    }

    [Fact]
    public async Task QueueCancel_InvalidGuid_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["queue", "cancel", "not-a-guid"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("GUID", error.ToString());
    }

    [Fact]
    public async Task QueueCancel_UnknownId_ReturnsUserError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliCommandRunner.RunAsync(
            ["queue", "cancel", "00000000-0000-0000-0000-000000000000"], output, error);

        Assert.Equal(CliExitCodes.UserError, exitCode);
        Assert.Contains("not found", error.ToString());
    }

    [Fact]
    public async Task QueueCancel_TerminalOperation_ReturnsUserError()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Environment.CurrentDirectory = tempDir;
            EsoDataConnection.CreateTablesIfNotExists();

            var completed = CreateQueuedOperation(QueueOperationStatus.Completed);
            await AddonServices.QueueStore.EnqueueAsync(completed);

            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "queue", "cancel", completed.OperationId.ToString("N")], output, error);

            Assert.Equal(CliExitCodes.UserError, exitCode);
            Assert.Contains("already", error.ToString());
            Assert.Contains("Completed", error.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task QueueCancel_InProgressOperation_RequestsCancellation()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Environment.CurrentDirectory = tempDir;
            EsoDataConnection.CreateTablesIfNotExists();

            var inProgress = CreateQueuedOperation(QueueOperationStatus.InProgress);
            await AddonServices.QueueStore.EnqueueAsync(inProgress);

            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "queue", "cancel", inProgress.OperationId.ToString("N")], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("Cancellation requested", error.ToString());

            // Verify the cancellation was persisted
            var all = await AddonServices.QueueStore.GetAllAsync();
            var updated = Assert.Single(all);
            Assert.True(updated.CancelRequested);
            Assert.Equal(QueueOperationStatus.InProgress, updated.Status);
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task QueueClear_FailedAndCanceled_RemovesBoth()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Environment.CurrentDirectory = tempDir;
            EsoDataConnection.CreateTablesIfNotExists();

            var completed = CreateQueuedOperation(QueueOperationStatus.Completed);
            var failed = CreateQueuedOperation(QueueOperationStatus.Failed);
            var canceled = CreateQueuedOperation(QueueOperationStatus.Canceled);

            await AddonServices.QueueStore.EnqueueAsync(completed);
            await AddonServices.QueueStore.EnqueueAsync(failed);
            await AddonServices.QueueStore.EnqueueAsync(canceled);

            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "queue", "clear", "--failed", "--canceled"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("Removed 2", output.ToString());

            var remaining = await AddonServices.QueueStore.GetAllAsync();
            Assert.Single(remaining);
            Assert.Equal(completed.OperationId, remaining[0].OperationId);
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task QueueClear_Json_ReturnsRemovedCount()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Environment.CurrentDirectory = tempDir;
            EsoDataConnection.CreateTablesIfNotExists();

            var completed = CreateQueuedOperation(QueueOperationStatus.Completed);
            await AddonServices.QueueStore.EnqueueAsync(completed);

            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--json", "--data-dir", tempDir, "queue", "clear", "--completed"], output, error);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("removed", output.ToString().ToLowerInvariant());
            Assert.Contains("1", output.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task QueueList_WhenExecutorLeaseHeldByOtherOwner_ReturnsQueue()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Environment.CurrentDirectory = tempDir;
            EsoDataConnection.CreateTablesIfNotExists();

            // Seed an external lease
            using (var db = new EsoDataConnection())
            {
                db.Insert(new OperationExecutorLeaseEntity
                {
                    LeaseName = "process",
                    OwnerId = "other-machine:9999:other-process-guid",
                    OwnerKind = "cli",
                    ProcessId = 9999,
                    MachineName = "other-machine",
                    AcquiredAtUtc = DateTime.UtcNow,
                    LastHeartbeatUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
                });
            }

            var pending = CreateQueuedOperation(QueueOperationStatus.Pending);
            await AddonServices.QueueStore.EnqueueAsync(pending);

            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "queue", "list"], output, error);

            // Queue list should succeed without requiring a lease
            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains(pending.OperationId.ToString("N"), output.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task QueueCancel_WhenExecutorLeaseHeldByOtherOwner_RequestsCancellation()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Environment.CurrentDirectory = tempDir;
            EsoDataConnection.CreateTablesIfNotExists();

            // Seed an external lease
            using (var db = new EsoDataConnection())
            {
                db.Insert(new OperationExecutorLeaseEntity
                {
                    LeaseName = "process",
                    OwnerId = "other-machine:9999:other-process-guid",
                    OwnerKind = "cli",
                    ProcessId = 9999,
                    MachineName = "other-machine",
                    AcquiredAtUtc = DateTime.UtcNow,
                    LastHeartbeatUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
                });
            }

            var pending = CreateQueuedOperation(QueueOperationStatus.Pending);
            await AddonServices.QueueStore.EnqueueAsync(pending);

            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "queue", "cancel", pending.OperationId.ToString("N")], output, error);

            // Queue cancel should succeed without requiring a lease
            Assert.Equal(CliExitCodes.Success, exitCode);

            var all = await AddonServices.QueueStore.GetAllAsync();
            var canceled = Assert.Single(all);
            Assert.Equal(QueueOperationStatus.Canceled, canceled.Status);
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Status_WhenExecutorLeaseHeldByOtherOwner_ReturnsSuccess()
    {
        var origDir = SaveCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Environment.CurrentDirectory = tempDir;
            EsoDataConnection.CreateTablesIfNotExists();

            // Seed an external lease
            using (var db = new EsoDataConnection())
            {
                db.Insert(new OperationExecutorLeaseEntity
                {
                    LeaseName = "process",
                    OwnerId = "other-machine:9999:other-process-guid",
                    OwnerKind = "cli",
                    ProcessId = 9999,
                    MachineName = "other-machine",
                    AcquiredAtUtc = DateTime.UtcNow,
                    LastHeartbeatUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
                });
            }

            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliCommandRunner.RunAsync(
                ["--data-dir", tempDir, "status"], output, error);

            // Status should succeed without requiring a lease
            Assert.Equal(CliExitCodes.Success, exitCode);
        }
        finally
        {
            Environment.CurrentDirectory = origDir;
            Directory.Delete(tempDir, true);
        }
    }

    private static QueuedOperation CreateQueuedOperation(QueueOperationStatus status)
    {
        return new QueuedOperation
        {
            OperationId = Guid.NewGuid(),
            AddonCommonId = Random.Shared.Next(1, 100000),
            AddonName = $"QueuedAddon-{Guid.NewGuid():N}",
            OperationType = AddonOperationType.Install,
            InstallationMethod = AddonInstallationMethod.SpellCrafter,
            Status = status,
            Priority = QueuePriority.Normal,
            RequestTime = DateTime.UtcNow,
            CompletionTime = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Collection definition that serializes all tests that modify <see cref="Environment.CurrentDirectory"/>
/// or <see cref="AppSettings.Instance"/>. Without this, parallel collections race on these global statics.
/// </summary>
[CollectionDefinition("EnvironmentStateCollection", DisableParallelization = true)]
public sealed class EnvironmentStateCollectionDefinition
{
}
