using SpellCrafter.Data;
using SpellCrafter.Models;

namespace SpellCrafter.Tests.IntegrationTests;

public sealed class CheckTableExistsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _dir;

    public CheckTableExistsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SpellCrafterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "test.db");
    }

    [Fact]
    public void CheckTableExists_ReturnsFalse_WhenTableDoesNotExist()
    {
        using var db = new EsoDataConnection(_dbPath);

        var exists = db.CheckTableExists<AddonOperationJournal>();

        Assert.False(exists);
    }

    [Fact]
    public void CheckTableExists_ReturnsTrue_AfterCreateTableIfNotExists()
    {
        using var db = new EsoDataConnection(_dbPath);
        db.CreateTableIfNotExists<CommonAddon>();

        var exists = db.CheckTableExists<CommonAddon>();

        Assert.True(exists);
    }

    [Fact]
    public void CreateTableIfNotExists_IsIdempotent()
    {
        using var db = new EsoDataConnection(_dbPath);

        db.CreateTableIfNotExists<CommonAddon>();
        db.CreateTableIfNotExists<CommonAddon>(); // Should not throw

        var exists = db.CheckTableExists<CommonAddon>();
        Assert.True(exists);
    }

    [Fact]
    public void CreateTablesIfNotExists_Instance_CreatesAllTables()
    {
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

        Assert.True(db.CheckTableExists<CommonAddon>());
        Assert.True(db.CheckTableExists<LocalAddon>());
        Assert.True(db.CheckTableExists<OnlineAddon>());
        Assert.True(db.CheckTableExists<Author>());
        Assert.True(db.CheckTableExists<Category>());
        Assert.True(db.CheckTableExists<AddonAuthor>());
        Assert.True(db.CheckTableExists<AddonCategory>());
        Assert.True(db.CheckTableExists<LocalAddonDependency>());
        Assert.True(db.CheckTableExists<OnlineAddonDependency>());
        Assert.True(db.CheckTableExists<AddonOperationJournal>());
    }

    [Fact]
    public void CreateTableIfNotExists_Instance_IsIdempotent()
    {
        using var db = new EsoDataConnection(_dbPath);

        db.CreateTableIfNotExists<CommonAddon>();
        db.CreateTableIfNotExists<CommonAddon>(); // Should not throw

        Assert.True(db.CheckTableExists<CommonAddon>());
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