using SpellCrafter.Data;

namespace SpellCrafter.Tests.TestInfrastructure;

public sealed class TempDatabaseFixture : IDisposable
{
    public string DatabasePath { get; }
    private bool _tablesCreated;

    public TempDatabaseFixture()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        DatabasePath = Path.Combine(dir, "test.db");
    }

    public EsoDataConnection CreateConnection()
    {
        var db = new EsoDataConnection(DatabasePath);
        if (!_tablesCreated)
        {
            EsoDataConnection.CreateTablesIfNotExists();
            _tablesCreated = true;
        }

        return db;
    }

    public void Dispose()
    {
        try
        {
            var dir = Path.GetDirectoryName(DatabasePath);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}