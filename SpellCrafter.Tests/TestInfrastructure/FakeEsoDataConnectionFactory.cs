using SpellCrafter.Data;
using SpellCrafter.Models;

namespace SpellCrafter.Tests.TestInfrastructure;

/// <summary>
/// Creates EsoDataConnection instances backed by a unique temporary database file.
/// The database file is created in the system temp directory and deleted on dispose.
/// </summary>
public sealed class FakeEsoDataConnectionFactory : IEsoDataConnectionFactory, IDisposable
{
    private readonly string _dbPath;
    private readonly string _dir;

    public FakeEsoDataConnectionFactory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "test.db");
        // Initialize all known tables on the temp DB directly (not via the static
        // CreateTablesIfNotExists which uses the default "ESOAddons.db" path).
        using var db = new EsoDataConnection(_dbPath);
        db.CreateTableIfNotExists<CommonAddon>();
        db.CreateTableIfNotExists<LocalAddon>();
        db.CreateTableIfNotExists<OnlineAddon>();
        db.CreateTableIfNotExists<Author>();
        db.CreateTableIfNotExists<Category>();
        db.CreateTableIfNotExists<AddonAuthor>();
        db.CreateTableIfNotExists<AddonCategory>();
        db.CreateTableIfNotExists<LocalAddonDependency>();
        db.CreateTableIfNotExists<OnlineAddonDependency>();
        db.CreateTableIfNotExists<AddonOperationJournal>();
        db.CreateTableIfNotExists<QueuedOperationEntity>();
        db.CreateTableIfNotExists<OperationExecutorLeaseEntity>();
    }

    public EsoDataConnection CreateConnection()
    {
        return new EsoDataConnection(_dbPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
