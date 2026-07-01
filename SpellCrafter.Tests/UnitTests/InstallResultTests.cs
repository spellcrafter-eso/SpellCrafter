using SpellCrafter.Models;

namespace SpellCrafter.Tests.UnitTests;

public sealed class InstallResultTests
{
    [Fact]
    public void Success_Defaults()
    {
        var result = InstallResult.Success();
        Assert.True(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.False(result.CompletedAfterCancellation);
        Assert.Null(result.WarningMessage);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Success_WithMessage()
    {
        var result = InstallResult.Success("warning");
        Assert.True(result.Succeeded);
        Assert.Equal("warning", result.WarningMessage);
    }

    [Fact]
    public void Success_CompletedAfterCancellation()
    {
        var result = InstallResult.Success("completed after cancel", true);
        Assert.True(result.Succeeded);
        Assert.True(result.CompletedAfterCancellation);
        Assert.Equal("completed after cancel", result.WarningMessage);
    }

    [Fact]
    public void Failure_Defaults()
    {
        var result = InstallResult.Failure("error");
        Assert.False(result.Succeeded);
        Assert.False(result.WasCanceled);
        Assert.Equal("error", result.ErrorMessage);
        Assert.Null(result.Exception);
        Assert.Null(result.BackupPath);
    }

    [Fact]
    public void Failure_WithException()
    {
        var inner = new InvalidOperationException("inner");
        var result = InstallResult.Failure("error", inner);
        Assert.False(result.Succeeded);
        Assert.Equal("error", result.ErrorMessage);
        Assert.Same(inner, result.Exception);
    }

    [Fact]
    public void Failure_WithBackupPath()
    {
        var result = InstallResult.Failure("error", backupPath: "/backup/path");
        Assert.False(result.Succeeded);
        Assert.Equal("/backup/path", result.BackupPath);
    }

    [Fact]
    public void Canceled_Defaults()
    {
        var result = InstallResult.Canceled("canceled");
        Assert.False(result.Succeeded);
        Assert.True(result.WasCanceled);
        Assert.Equal("canceled", result.ErrorMessage);
    }

    [Fact]
    public void Canceled_WithExceptionAndBackup()
    {
        var ex = new OperationCanceledException();
        var result = InstallResult.Canceled("canceled", ex, "/backup");
        Assert.True(result.WasCanceled);
        Assert.Same(ex, result.Exception);
        Assert.Equal("/backup", result.BackupPath);
    }
}