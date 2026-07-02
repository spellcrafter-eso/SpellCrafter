using System;
using SQLite;

namespace SpellCrafter.Models;

[Table("OperationExecutorLease")]
public sealed class OperationExecutorLeaseEntity
{
    [PrimaryKey] public string LeaseName { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;

    public string OwnerKind { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public string MachineName { get; set; } = string.Empty;

    public DateTime AcquiredAtUtc { get; set; }

    public DateTime LastHeartbeatUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public static OperationExecutorLeaseEntity FromLease(OperationExecutorLease lease)
    {
        return new OperationExecutorLeaseEntity
        {
            LeaseName = lease.LeaseName,
            OwnerId = lease.OwnerId,
            OwnerKind = lease.OwnerKind,
            ProcessId = lease.ProcessId,
            MachineName = lease.MachineName,
            AcquiredAtUtc = lease.AcquiredAtUtc,
            LastHeartbeatUtc = lease.LastHeartbeatUtc,
            ExpiresAtUtc = lease.ExpiresAtUtc
        };
    }

    public OperationExecutorLease ToLease()
    {
        return new OperationExecutorLease
        {
            LeaseName = LeaseName,
            OwnerId = OwnerId,
            OwnerKind = OwnerKind,
            ProcessId = ProcessId,
            MachineName = MachineName,
            AcquiredAtUtc = AcquiredAtUtc,
            LastHeartbeatUtc = LastHeartbeatUtc,
            ExpiresAtUtc = ExpiresAtUtc
        };
    }
}
