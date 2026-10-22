using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Data.Extensions;

/// <summary>
/// Serializes check-then-insert writes that no unique index backs, so two requests sent
/// a moment apart (a double-click, a repeated Enter) cannot both pass the check.
/// </summary>
public static class AdvisoryLockExtensions
{
    /// <summary>
    /// Takes a PostgreSQL transaction-scoped advisory lock on (<paramref name="scope"/>,
    /// <paramref name="key"/>), waiting while another transaction holds it. The lock is released
    /// when the current transaction commits or rolls back, so the caller must already be inside one.
    /// A no-op on providers other than PostgreSQL (the InMemory test database).
    /// </summary>
    /// <param name="db">DbContext whose open transaction holds the lock.</param>
    /// <param name="scope">What is being serialized, e.g. <c>time-entries.timer</c>.</param>
    /// <param name="key">Which instance of it, e.g. the user id; empty for one lock across the install.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task AcquireAdvisoryLockAsync(
        this AppDbContext db, string scope, string key, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
            return;

        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("An advisory lock needs an open transaction to hold it.");

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({scope}), hashtext({key}))", ct);
    }
}
