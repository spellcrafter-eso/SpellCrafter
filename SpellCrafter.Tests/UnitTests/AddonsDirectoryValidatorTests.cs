using SpellCrafter.Services;
using SpellCrafter.Tests.TestInfrastructure;

namespace SpellCrafter.Tests.UnitTests;

public sealed class AddonsDirectoryValidatorTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValid_NullEmptyOrWhitespace(string? path, bool expected)
    {
        Assert.Equal(expected, AddonsDirectoryValidator.IsValidAddonsDirectory(path));
    }

    [Theory]
    [InlineData("/some/path/AddOns", true)]
    [InlineData("/some/path/addons", true)]
    [InlineData("/some/path/ADDONS", true)]
    [InlineData("/some/path/AddOnsExtra", false)]
    [InlineData("/some/path/NotAddOns", false)]
    [InlineData("/some/path/AddOn", false)]
    public void IsValid_VariousPaths(string path, bool expected)
    {
        Assert.Equal(expected, AddonsDirectoryValidator.IsValidAddonsDirectory(path));
    }

    [Theory]
    [InlineData(null, "AddOns directory path is empty.")]
    [InlineData("", "AddOns directory path is empty.")]
    [InlineData("   ", "AddOns directory path is empty.")]
    public void GetValidationError_NullEmpty(string? path, string expectedMessage)
    {
        var error = AddonsDirectoryValidator.GetValidationError(path);
        Assert.Contains(expectedMessage, error);
    }

    [Fact]
    public void GetValidationError_NonExistentDirectory()
    {
        var path = "/nonexistent/path/AddOns";
        var error = AddonsDirectoryValidator.GetValidationError(path);
        Assert.Contains("does not exist", error);
    }

    [Fact]
    public void GetValidationError_WrongDirectoryName()
    {
        using var tmp = new TempDirectoryFixture();
        var wrongPath = Path.Combine(tmp.RootPath, "WrongName");
        Directory.CreateDirectory(wrongPath);

        var error = AddonsDirectoryValidator.GetValidationError(wrongPath);
        Assert.Contains("must be named", error);
        Assert.Contains("WrongName", error);
    }

    [Fact]
    public void GetValidationError_ValidDirectory_ReturnsNull()
    {
        using var tmp = new TempDirectoryFixture();

        var error = AddonsDirectoryValidator.GetValidationError(tmp.AddonsPath);
        Assert.Null(error);
    }
}