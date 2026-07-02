using System;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

public interface IAddonOperationLeaseStore
{
    const string DefaultLeaseName = "operation-executor";

    Task<OperationExecutorLease?> GetLeaseAsync(CancellationToken cancellationToken = default);

    Task<OperationExecutorLeaseHandle?> TryAcquireAsync(
        string ownerKind,
        string ownerId,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default);

    Task<bool> RenewAsync(
        string ownerId,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseAsync(string ownerId, CancellationToken cancellationToken = default);
}
