namespace SpellCrafter.Models;

/// <summary>
/// Priority level for queued operations.
/// Higher-priority operations are claimed before lower-priority ones.
/// </summary>
public enum QueuePriority
{
    /// <summary>Default priority for user-initiated operations (install, update, delete).</summary>
    Normal = 0,

    /// <summary>Background cleanup operations (orphan dependency removal).</summary>
    Low = 1
}