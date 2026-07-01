using System;

namespace SpellCrafter.Tests.TestInfrastructure;

public sealed class TempDirectoryFixture : IDisposable
{
    public string RootPath { get; }
    public string AddonsPath { get; }
    public string OperationsPath { get; }

    public TempDirectoryFixture()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        AddonsPath = Path.Combine(RootPath, "AddOns");
        OperationsPath = Path.Combine(RootPath, "Operations");
        Directory.CreateDirectory(AddonsPath);
        Directory.CreateDirectory(OperationsPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}