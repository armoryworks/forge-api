using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Forge.Data.Migrations;

/// <summary>
/// Reconciles <c>__EFMigrationsHistory</c> after a migration <b>squash</b>.
///
/// <para>The problem this solves: when 132 timestamped migrations are collapsed into a single
/// <c>InitialBaseline</c>, an existing install's history still holds the 132 old IDs. EF sees the
/// baseline as <i>pending</i> (its ID isn't in history) and <see cref="RelationalDatabaseFacadeExtensions.MigrateAsync"/>
/// tries to <c>CREATE TABLE</c> everything — which fails because the tables already exist.</para>
///
/// <para>This complements <see cref="MigrationSchemaVerifier"/>, which handles the <i>missing</i>-history
/// case. Here history is present but <i>stale</i>: it names migrations the assembly no longer knows.</para>
///
/// <para><b>Fail-safe by design.</b> History is only rewritten when every assembly migration not yet in
/// history (the baseline) is verified present in the live schema. If anything is unverified, the
/// reconciler does nothing and reports why — never risking a destructive apply against a mismatched
/// schema.</para>
/// </summary>
public static class StaleMigrationHistoryReconciler
{
    /// <summary>Outcome of a reconciliation attempt. Pure data — the caller logs/decides.</summary>
    public sealed record Result(
        bool Reconciled,
        int StaleRemoved,
        int BaselineInserted,
        IReadOnlyList<string> StaleMigrationIds,
        IReadOnlyList<string> PendingMigrations,
        IReadOnlyList<string> UnverifiedAssemblyMigrations,
        string? AbortReason);

    /// <summary>
    /// Reconciles stale history if and only if the signature is present (history names migrations the
    /// assembly doesn't) AND every baseline migration verifies present in the live schema.
    /// </summary>
    /// <param name="db">Context bound to the live (relational) database.</param>
    /// <param name="appliedMigrationIds">IDs currently in <c>__EFMigrationsHistory</c>.</param>
    /// <param name="assemblyMigrationIds">IDs the migrations assembly knows (post-squash: just the baseline).</param>
    /// <param name="migrationsAssembly">Used to instantiate migrations for schema verification.</param>
    /// <param name="productVersion">EF product version stamped into inserted history rows.</param>
    public static async Task<Result> ReconcileAsync(
        DbContext db,
        IReadOnlyList<string> appliedMigrationIds,
        IReadOnlyList<string> assemblyMigrationIds,
        IMigrationsAssembly migrationsAssembly,
        string productVersion)
    {
        var assemblySet = assemblyMigrationIds.ToHashSet(StringComparer.Ordinal);
        var appliedSet = appliedMigrationIds.ToHashSet(StringComparer.Ordinal);

        // Signature: applied IDs the assembly no longer knows. After a squash this is the old chain.
        var staleApplied = appliedMigrationIds.Where(id => !assemblySet.Contains(id)).ToList();
        if (staleApplied.Count == 0)
            return new Result(false, 0, 0, [], [], [], "no-stale-history");

        // Assembly migrations not yet in history. Partition them:
        //   verified → their schema is already present (the baseline the squash collapsed to);
        //   pending  → genuinely new work not yet applied (e.g. a migration added AFTER the squash).
        // Pending migrations must NOT block reconciliation — they are left out of history so the normal
        // MigrateAsync that follows applies them. (Earlier this aborted on any unverified migration,
        // which wrongly tripped on legitimate post-squash migrations.)
        var assemblyNotApplied = assemblyMigrationIds.Where(id => !appliedSet.Contains(id)).ToList();

        var verified = new List<string>();
        var pending = new List<string>();
        foreach (var migrationId in assemblyNotApplied)
        {
            if (migrationsAssembly.Migrations.TryGetValue(migrationId, out var typeInfo)
                && await MigrationSchemaVerifier.IsMigrationApplied(
                    db, (Migration)Activator.CreateInstance(typeInfo.AsType())!, migrationId))
                verified.Add(migrationId);
            else
                pending.Add(migrationId);
        }

        // Fail safe: the baseline (first assembly migration) MUST be present — already in history, or
        // verified present now. If it isn't, the live schema doesn't match the baseline; do NOT rewrite
        // history (a destructive MigrateAsync could follow). Leave everything untouched and surface it.
        var baselineId = assemblyMigrationIds.Count > 0 ? assemblyMigrationIds[0] : null;
        var baselinePresent = baselineId != null
            && (appliedSet.Contains(baselineId) || verified.Contains(baselineId));
        if (!baselinePresent)
            return new Result(false, 0, 0, staleApplied, pending, pending, "baseline-not-present");

        // Baseline present → rewrite history in one transaction; pending migrations stay out (MigrateAsync applies them).
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();

            // 1. Back up the old rows so a rollback can restore them (squash plan §3.6).
            //    IF NOT EXISTS preserves the original pre-squash snapshot across repeated boots.
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory_pre_squash" AS
                SELECT * FROM "__EFMigrationsHistory"
                """);

            // 2. Delete the stale rows (per-id to avoid array-parameter binding; provider-neutral).
            foreach (var staleId in staleApplied)
            {
                await db.Database.ExecuteSqlRawAsync(
                    """
                    DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = {0}
                    """,
                    staleId);
            }

            // 3. Insert the verified baseline IDs (idempotent on re-run).
            foreach (var migrationId in verified)
            {
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                    VALUES ({0}, {1})
                    ON CONFLICT ("MigrationId") DO NOTHING
                    """,
                    migrationId, productVersion);
            }

            await tx.CommitAsync();
        });

        return new Result(true, staleApplied.Count, verified.Count, staleApplied, pending, [], null);
    }
}
