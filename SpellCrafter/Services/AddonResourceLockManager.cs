using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

/// <summary>
/// Manages per-resource semaphores to allow concurrent operations on
/// independent resources while serializing operations on the same resource.
/// </summary>
public sealed class AddonResourceLockManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>
    /// Acquires exclusive locks for all given resource keys.
    /// Locks are acquired in a deterministic order (sorted by key) to prevent deadlocks.
    /// Returns a disposable handle; call Dispose to release all acquired locks.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(
        IReadOnlySet<AddonResourceKey> resourceKeys,
        CancellationToken cancellationToken = default)
    {
        if (resourceKeys == null)
            throw new ArgumentNullException(nameof(resourceKeys));

        if (resourceKeys.Count == 0)
            return NullDisposable.Instance;

        var sortedKeys = resourceKeys
            .Select(k => k.ToString())
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        var acquired = new List<SemaphoreSlim>(sortedKeys.Count);

        try
        {
            foreach (var key in sortedKeys)
            {
                var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
                await semaphore.WaitAsync(cancellationToken);
                acquired.Add(semaphore);
            }

            return new ResourceLockHandle(acquired);
        }
        catch
        {
            foreach (var sem in acquired)
                sem.Release();

            throw;
        }
    }

    /// <summary>
    /// Acquires locks for a single resource key.
    /// </summary>
    public Task<IDisposable> AcquireAsync(
        AddonResourceKey resourceKey,
        CancellationToken cancellationToken = default)
    {
        return AcquireAsync(new HashSet<AddonResourceKey> { resourceKey }, cancellationToken);
    }

    private sealed class ResourceLockHandle : IDisposable
    {
        private readonly List<SemaphoreSlim> _acquired;

        public ResourceLockHandle(List<SemaphoreSlim> acquired)
        {
            _acquired = acquired;
        }

        public void Dispose()
        {
            foreach (var sem in _acquired)
                sem.Release();
        }
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}