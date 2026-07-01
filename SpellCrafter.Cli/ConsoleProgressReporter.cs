using System;
using System.IO;
using SpellCrafter.Models;

namespace SpellCrafter.Cli;

internal sealed class ConsoleProgressReporter : IProgress<InstallProgress>
{
    private readonly TextWriter _errorWriter;
    private readonly bool _suppressed;

    public ConsoleProgressReporter(TextWriter errorWriter, bool suppressed = false)
    {
        _errorWriter = errorWriter ?? throw new ArgumentNullException(nameof(errorWriter));
        _suppressed = suppressed;
    }

    public void Report(InstallProgress value)
    {
        if (_suppressed)
            return;

        _errorWriter.WriteLine($"[{value.AddonName}] {value.Operation}/{value.Stage}: {value.Message}");
    }
}