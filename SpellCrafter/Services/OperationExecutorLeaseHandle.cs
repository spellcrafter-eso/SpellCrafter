using System;
using System.Threading.Tasks;

namespace SpellCrafter.Services;

public sealed class OperationExecutorLeaseHandle : IAsyncDisposable
{
    private readonly IAddonOperationLeaseStore _leaseStore;
    private bool _disposed;

    internal OperationExecutorLeaseHandle(
        IAddonOperationLeaseStore leaseStore,
        string ownerId)
    {
        _leaseStore = leaseStore ?? throw new ArgumentNullException(nameof(leaseStore));
        OwnerId = ownerId;
    }

    public string OwnerId { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _leaseStore.ReleaseAsync(OwnerId);
        _disposed = true;
    }
}
