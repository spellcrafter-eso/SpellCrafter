using SpellCrafter.Services;

namespace SpellCrafter.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="AddonVersionComparer.CompareVersions"/>.
/// Note: the Compare method follows IComparer{T} semantics — any negative value means "less than",
/// any positive value means "greater than". We assert the sign rather than exact values because
/// string.Compare can return arbitrary negative/positive numbers.
/// </summary>
public sealed class VersionComparerTests
{
    // ---- Null ----

    [Fact]
    public void Compare_BothNull_ReturnsZero()
    {
        string? v1 = null;
        string? v2 = null;
        Assert.Equal(0, AddonVersionComparer.CompareVersions(v1, v2));
    }

    [Fact]
    public void Compare_FirstNull_ReturnsNegative()
    {
        string? v1 = null;
        Assert.True(AddonVersionComparer.CompareVersions(v1, "1.0") < 0);
    }

    [Fact]
    public void Compare_SecondNull_ReturnsPositive()
    {
        string? v2 = null;
        Assert.True(AddonVersionComparer.CompareVersions("1.0", v2) > 0);
    }

    // ---- Empty ----

    [Fact]
    public void Compare_BothEmpty_ReturnsZero()
    {
        Assert.Equal(0, AddonVersionComparer.CompareVersions("", ""));
    }

    [Fact]
    public void Compare_FirstEmpty_ReturnsNegative()
    {
        Assert.True(AddonVersionComparer.CompareVersions("", "1.0") < 0);
    }

    [Fact]
    public void Compare_SecondEmpty_ReturnsPositive()
    {
        Assert.True(AddonVersionComparer.CompareVersions("1.0", "") > 0);
    }

    // ---- Simple numeric ----

    [Theory]
    [InlineData("1.0", "2.0", -1)]
    [InlineData("2.0", "1.0", 1)]
    [InlineData("1.0", "1.0", 0)]
    public void Compare_Simple(string v1, string v2, int expectedSign)
    {
        var result = AddonVersionComparer.CompareVersions(v1, v2);
        AssertSign(expectedSign, result);
    }

    // ---- Three parts ----

    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    public void Compare_ThreeParts(string v1, string v2, int expectedSign)
    {
        var result = AddonVersionComparer.CompareVersions(v1, v2);
        AssertSign(expectedSign, result);
    }

    // ---- Numeric (not lexicographic) ----

    [Theory]
    [InlineData("1.2.3", "1.10.0", -1)]
    [InlineData("1.10.0", "1.2.3", 1)]
    public void Compare_NumericNotLexicographic(string v1, string v2, int expectedSign)
    {
        var result = AddonVersionComparer.CompareVersions(v1, v2);
        AssertSign(expectedSign, result);
    }

    // ---- Different segment counts ----

    [Theory]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0", 0)]
    [InlineData("1.0", "1.0.1", -1)]
    [InlineData("1.0.1", "1.0", 1)]
    public void Compare_DifferentSegmentCount(string v1, string v2, int expectedSign)
    {
        var result = AddonVersionComparer.CompareVersions(v1, v2);
        AssertSign(expectedSign, result);
    }

    // ---- Non-numeric segments ----

    [Theory]
    [InlineData("abc", "def", -1)]
    [InlineData("def", "abc", 1)]
    [InlineData("abc", "abc", 0)]
    public void Compare_NonNumericSegments(string v1, string v2, int expectedSign)
    {
        var result = AddonVersionComparer.CompareVersions(v1, v2);
        AssertSign(expectedSign, result);
    }

    // ---- Mixed numeric and alpha ----

    [Theory]
    [InlineData("1.a.3", "1.b.3", -1)]
    [InlineData("1.2", "1.a", -1)]
    public void Compare_MixedNumericAlpha(string v1, string v2, int expectedSign)
    {
        var result = AddonVersionComparer.CompareVersions(v1, v2);
        AssertSign(expectedSign, result);
    }

    // ---- Zero-prefixed parts ----

    [Fact]
    public void Compare_ZeroPrefixedParts()
    {
        Assert.Equal(0, AddonVersionComparer.CompareVersions("1.0", "1.00"));
        Assert.Equal(0, AddonVersionComparer.CompareVersions("01.0", "1.0"));
        Assert.Equal(0, AddonVersionComparer.CompareVersions("1.01", "1.1"));
    }

    // ---- VPREFIX: The version comparer does NOT strip 'v' / 'V' prefix.
    // "v1.0" is treated as ["v1", ""] vs ["1", ""]; "v1" vs "1" is a string comparison.
    // 'v' (ASCII 118) > '1'/'2' so "v1.0" > any version starting with a digit.
    // These tests document the ACTUAL behavior rather than expected ideal behavior. ----

    [Theory]
    [InlineData("v1.0", "1.0")] // "v1" > "1"
    [InlineData("v2.0", "1.0")] // "v2" > "1"
    [InlineData("v1.0", "2.0")] // "v1" > "2"
    public void Compare_VPrefix_DoesNotStrip(string v1, string v2)
    {
        var result = AddonVersionComparer.CompareVersions(v1, v2);
        Assert.True(result > 0, $"Expected {v1} > {v2} but got {result}");
    }

    // ---- Non-numeric characters in version ----

    [Fact]
    public void Compare_WithDashSuffix()
    {
        // "1.0-beta" and "1.0" are not equal because "-beta" vs "" is a string comparison
        var result = AddonVersionComparer.CompareVersions("1.0-beta", "1.0");
        Assert.NotEqual(0, result);
    }

    // ---- Helper ----

    private static void AssertSign(int expectedSign, int actual)
    {
        if (expectedSign == 0)
            Assert.Equal(0, actual);
        else if (expectedSign < 0)
            Assert.True(actual < 0, $"Expected negative value but got {actual}");
        else
            Assert.True(actual > 0, $"Expected positive value but got {actual}");
    }
}