using System;

namespace SpellCrafter.Models;

public sealed class OperationExecutorLease
{
    public string LeaseName { get; init; } = string.Empty;

    public string OwnerId { get; init; } = string.Empty;

    public string OwnerKind { get; init; } = string.Empty;

    public int ProcessId { get; init; }

    public string MachineName { get; init; } = string.Empty;

    public DateTime AcquiredAtUtc { get; init; }

    public DateTime LastHeartbeatUtc { get; init; }

    public DateTime ExpiresAtUtc { get; init; }

    public bool IsExpired(DateTime utcNow) => ExpiresAtUtc <= utcNow;
}
