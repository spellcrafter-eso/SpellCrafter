namespace SpellCrafter.Models;

/// <summary>
/// Identifies a resource that can be locked for an operation.
/// Kind is "addon" for addon:{CommonAddonId} or "folder" for folder:{normalizedPath}.
/// </summary>
public sealed record AddonResourceKey(string Kind, string Key)
{
    public static AddonResourceKey ForAddon(int commonAddonId)
    {
        return new AddonResourceKey("addon", commonAddonId.ToString());
    }

    public static AddonResourceKey ForFolder(string normalizedPath)
    {
        return new AddonResourceKey("folder", normalizedPath);
    }

    public override string ToString()
    {
        return $"{Kind}:{Key}";
    }
}