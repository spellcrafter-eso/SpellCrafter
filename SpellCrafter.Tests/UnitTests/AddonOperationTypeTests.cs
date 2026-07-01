using SpellCrafter.Models;

namespace SpellCrafter.Tests.UnitTests;

public sealed class AddonOperationTypeTests
{
    [Fact]
    public void Install_HasExpectedValue()
    {
        Assert.Equal("install", AddonOperationType.Install);
    }

    [Fact]
    public void Update_HasExpectedValue()
    {
        Assert.Equal("update", AddonOperationType.Update);
    }

    [Fact]
    public void Reinstall_HasExpectedValue()
    {
        Assert.Equal("reinstall", AddonOperationType.Reinstall);
    }

    [Fact]
    public void Delete_HasExpectedValue()
    {
        Assert.Equal("delete", AddonOperationType.Delete);
    }

    [Fact]
    public void AllTypes_AreUnique()
    {
        var types = new[] { AddonOperationType.Install, AddonOperationType.Update, AddonOperationType.Reinstall, AddonOperationType.Delete };
        Assert.Equal(4, new HashSet<string>(types).Count);
    }
}