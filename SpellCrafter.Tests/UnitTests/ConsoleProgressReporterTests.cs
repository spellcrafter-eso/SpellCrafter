using SpellCrafter.Cli;
using SpellCrafter.Models;

namespace SpellCrafter.Tests.UnitTests;

public sealed class ConsoleProgressReporterTests
{
    [Fact]
    public void Report_WritesToErrorWriter()
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);

        reporter.Report(new InstallProgress("TestAddon", "Install", InstallProgressStage.Downloading, "Downloading..."));

        var text = writer.ToString();
        Assert.Contains("[TestAddon]", text);
        Assert.Contains("Install/Downloading", text);
        Assert.Contains("Downloading...", text);
    }

    [Fact]
    public void Report_Suppressed_WritesNothing()
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer, true);

        reporter.Report(new InstallProgress("TestAddon", "Install", InstallProgressStage.Downloading, "Downloading..."));

        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void Report_MultipleCalls_AllAppear()
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);

        reporter.Report(new InstallProgress("A1", "Install", InstallProgressStage.Preparing, "prep"));
        reporter.Report(new InstallProgress("A1", "Install", InstallProgressStage.Downloading, "dl"));
        reporter.Report(new InstallProgress("A1", "Install", InstallProgressStage.Completed, "done"));

        var text = writer.ToString();
        Assert.Contains("prep", text);
        Assert.Contains("dl", text);
        Assert.Contains("done", text);
    }

    [Fact]
    public void Constructor_NullWriter_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ConsoleProgressReporter(null!));
    }
}