using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpellCrafter.Data;
using SpellCrafter.Models;

namespace SpellCrafter.Services;

/// <summary>
/// SQLite-backed persistent store for queued addon operations.
/// On construction, resets any InProgress operations back to Pending
/// to recover from a previous crash.
/// </summary>
public sealed class SqliteAddonOperationQueueStore : IAddonOperationQueueStore, IDisposable
{
    private readonly IEsoDataConnectionFactory _dbConnectionFactory;
    private readonly object _claimLock = new();
    private bool _disposed;

    public SqliteAddonOperationQueueStore(IEsoDataConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory
                               ?? throw new ArgumentNullException(nameof(dbConnectionFactory));

        EnsureTableExists();
        RecoverInProgressOperations();
    }

    private void EnsureTableExists()
    {
        using var db = _dbConnectionFactory.CreateConnection();
        db.CreateTableIfNotExists<QueuedOperationEntity>();
        EnsureColumnExists(db, "CancelRequested", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(db, "CancelReason", "TEXT NULL");
        EnsureColumnExists(db, "CancelRequestedAtUtc", "TEXT NULL");
        EnsureColumnExists(db, "ResultMessage", "TEXT NULL");
        EnsureColumnExists(db, "CompletedAfterCancellation", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void EnsureColumnExists(EsoDataConnection db, string columnName, string columnDefinition)
    {
        var columns = db.Query<TableColumnInfo>("PRAGMA table_info(QueuedOperation)");
        if (columns.Any(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)))
            return;

        db.Execute($"ALTER TABLE QueuedOperation ADD COLUMN {columnName} {columnDefinition}");
    }

    /// <summary>
    /// On startup, any operations left in InProgress state are recovered.
    /// If JournalCompleted is true, the worker/journal actions completed successfully
    /// but the queue status wasn't updated — transition directly to Completed.
    /// Otherwise, reset to Pending for retry.
    /// </summary>
    private void RecoverInProgressOperations()
    {
        using var db = _dbConnectionFactory.CreateConnection();

        var stuck = db.Table<QueuedOperationEntity>()
            .Where(e => e.Status == (int)QueueOperationStatus.InProgress)
            .ToList();

        foreach (var entity in stuck)
        {
            if (entity.JournalCompleted)
            {
                // Journal work is done; only the queue status is stale.
                entity.Status = (int)QueueOperationStatus.Completed;
                entity.CompletionTime ??= DateTime.UtcNow;
            }
            else
            {
                // Journal work may not be done; reset for retry.
                entity.Status = (int)QueueOperationStatus.Pending;
                entity.StartTime = null;
                entity.ErrorMessage = "Operation was interrupted by a previous crash. It will be retried.";
            }

            db.Update(entity);
        }
    }

    public Task EnqueueAsync(QueuedOperation operation, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Task.FromException(new ObjectDisposedException(nameof(SqliteAddonOperationQueueStore)));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var db = _dbConnectionFactory.CreateConnection();
            var entity = QueuedOperationEntity.FromQueuedOperation(operation);
            db.Insert(entity);
            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromException(ex);
        }
    }

    public Task<QueuedOperation?> TryClaimNextAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Task.FromException<QueuedOperation?>(
                new ObjectDisposedException(nameof(SqliteAddonOperationQueueStore)));

        cancellationToken.ThrowIfCancellationRequested();

        lock (_claimLock)
        {
            try
            {
                using var db = _dbConnectionFactory.CreateConnection();
                QueuedOperation? claimed = null;

                db.RunInTransaction(() =>
                {
                    var next = db.Table<QueuedOperationEntity>()
                        .Where(e => e.Status == (int)QueueOperationStatus.Pending)
                        .OrderBy(e => e.Priority)
                        .ThenBy(e => e.RequestTime)
                        .FirstOrDefault();

                    if (next == null)
                        return;

                    var startTime = DateTime.UtcNow;
                    var rowsChanged = db.Execute(
                        "UPDATE QueuedOperation SET Status = ?, StartTime = ? WHERE OperationId = ? AND Status = ?",
                        (int)QueueOperationStatus.InProgress,
                        startTime,
                        next.OperationId,
                        (int)QueueOperationStatus.Pending);

                    if (rowsChanged != 1)
                        return;

                    next.Status = (int)QueueOperationStatus.InProgress;
                    next.StartTime = startTime;
                    claimed = next.ToQueuedOperation();
                });

                return Task.FromResult(claimed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Task.FromException<QueuedOperation?>(ex);
            }
        }
    }

    public Task<IReadOnlyList<QueuedOperation>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Task.FromException<IReadOnlyList<QueuedOperation>>(
                new ObjectDisposedException(nameof(SqliteAddonOperationQueueStore)));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var db = _dbConnectionFactory.CreateConnection();

            var entities = db.Table<QueuedOperationEntity>()
                .OrderBy(e => e.Priority)
                .ThenBy(e => e.RequestTime)
                .ToList();

            var result = entities
                .Select(e => e.ToQueuedOperation())
                .ToList()
                .AsReadOnly();

            return Task.FromResult<IReadOnlyList<QueuedOperation>>(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromException<IReadOnlyList<QueuedOperation>>(ex);
        }
    }

    public Task UpdateStatusAsync(
        Guid operationId,
        QueueOperationStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Task.FromException(
                new ObjectDisposedException(nameof(SqliteAddonOperationQueueStore)));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var db = _dbConnectionFactory.CreateConnection();

            var key = operationId.ToString("N");
            var entity = db.Find<QueuedOperationEntity>(key);

            if (entity == null)
                return Task.CompletedTask; // Already removed — idempotent

            entity.Status = (int)status;

            if (status == QueueOperationStatus.InProgress)
                entity.StartTime ??= DateTime.UtcNow;

            if (status is QueueOperationStatus.Completed or QueueOperationStatus.Failed or QueueOperationStatus.Canceled)
                entity.CompletionTime = DateTime.UtcNow;

            entity.ErrorMessage = errorMessage;

            db.Update(entity);
            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromException(ex);
        }
    }

    public Task RemoveAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Task.FromException(
                new ObjectDisposedException(nameof(SqliteAddonOperationQueueStore)));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var db = _dbConnectionFactory.CreateConnection();

            var key = operationId.ToString("N");
            db.Delete<QueuedOperationEntity>(key);
            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromException(ex);
        }
    }

    public Task UpdateResultAsync(
        Guid operationId,
        string? resultMessage,
        bool completedAfterCancellation,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Task.FromException(
                new ObjectDisposedException(nameof(SqliteAddonOperationQueueStore)));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var db = _dbConnectionFactory.CreateConnection();

            var key = operationId.ToString("N");
            var entity = db.Find<QueuedOperationEntity>(key);

            if (entity == null)
                return Task.CompletedTask;

            entity.ResultMessage = resultMessage;
            entity.CompletedAfterCancellation = completedAfterCancellation;
            db.Update(entity);

            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromException(ex);
        }
    }

    public Task<int> RemoveTerminalAsync(
        IReadOnlySet<QueueOperationStatus> statuses,
        DateTime? completedBeforeUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Task.FromException<int>(
                new ObjectDisposedException(nameof(SqliteAddonOperationQueueStore)));

        cancellationToken.ThrowIfCancellationRequested();

        var terminalStatuses = GetRequestedTerminalStatuses(statuses);
        if (terminalStatuses.Count == 0)
            return Task.FromResult(0);

        try
        {
            using var db = _dbConnectionFactory.CreateConnection();

            var entities = db.Table<QueuedOperationEntity>()
                .ToList()
                .Where(e => terminalStatuses.Contains((QueueOperationStatus)e.Status))
                .Where(e => completedBeforeUtc == null ||
                            (e.CompletionTime != null && e.CompletionTime < completedBeforeUtc))
                .ToList();

            foreach (var entity in entities)
                db.Delete(entity);

            return Task.FromResult(entities.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromException<int>(ex);
        }
    }

    public Task<bool> RequestCancellationAsync(
        Guid operationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Task.FromException<bool>(
                new ObjectDisposedException(nameof(SqliteAddonOperationQueueStore)));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var db = _dbConnectionFactory.CreateConnection();

            var key = operationId.ToString("N");
            var now = DateTime.UtcNow;
            var requested = false;

            db.RunInTransaction(() =>
            {
                var entity = db.Find<QueuedOperationEntity>(key);
                if (entity == null)
                    return;

                var status = (QueueOperationStatus)entity.Status;
                if (status is QueueOperationStatus.Completed or QueueOperationStatus.Failed or QueueOperationStatus.Canceled)
                    return;

                if (status == QueueOperationStatus.Pending)
                {
                    var rowsChanged = db.Execute(
                        "UPDATE QueuedOperation " +
                        "SET Status = ?, CompletionTime = ?, ErrorMessage = ?, " +
                        "CancelRequested = ?, CancelReason = ?, CancelRequestedAtUtc = ? " +
                        "WHERE OperationId = ? AND Status = ?",
                        (int)QueueOperationStatus.Canceled,
                        now,
                        reason,
                        1,
                        reason,
                        now,
                        key,
                        (int)QueueOperationStatus.Pending);

                    requested = rowsChanged == 1;
                    if (requested)
                        return;

                    entity = db.Find<QueuedOperationEntity>(key);
                    status = entity == null
                        ? QueueOperationStatus.Completed
                        : (QueueOperationStatus)entity.Status;

                    if (status != QueueOperationStatus.InProgress)
                        return;

                    rowsChanged = db.Execute(
                        "UPDATE QueuedOperation " +
                        "SET CancelRequested = ?, CancelReason = ?, CancelRequestedAtUtc = ? " +
                        "WHERE OperationId = ? AND Status = ?",
                        1,
                        reason,
                        now,
                        key,
                        (int)QueueOperationStatus.InProgress);

                    requested = rowsChanged == 1;
                    return;
                }

                if (status == QueueOperationStatus.InProgress)
                {
                    var rowsChanged = db.Execute(
                        "UPDATE QueuedOperation " +
                        "SET CancelRequested = ?, CancelReason = ?, CancelRequestedAtUtc = ? " +
                        "WHERE OperationId = ? AND Status = ?",
                        1,
                        reason,
                        now,
                        key,
                        (int)QueueOperationStatus.InProgress);

                    requested = rowsChanged == 1;
                }
            });

            return Task.FromResult(requested);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromException<bool>(ex);
        }
    }

    public Task MarkJournalCompletedAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Task.FromException(new ObjectDisposedException(nameof(SqliteAddonOperationQueueStore)));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var db = _dbConnectionFactory.CreateConnection();

            var key = operationId.ToString("N");
            var entity = db.Find<QueuedOperationEntity>(key);

            if (entity == null)
                return Task.CompletedTask; // Already removed — idempotent

            entity.JournalCompleted = true;
            db.Update(entity);
            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromException(ex);
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private sealed class TableColumnInfo
    {
        public string Name { get; set; } = string.Empty;
    }

    private static HashSet<QueueOperationStatus> GetRequestedTerminalStatuses(
        IReadOnlySet<QueueOperationStatus> statuses)
    {
        var terminalStatuses = new HashSet<QueueOperationStatus>
        {
            QueueOperationStatus.Completed,
            QueueOperationStatus.Failed,
            QueueOperationStatus.Canceled
        };

        terminalStatuses.IntersectWith(statuses);
        return terminalStatuses;
    }
}
