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
            return new Result(false, 0, 0, [], [], "no-stale-history");

        // Assembly migrations not yet in history. Post-squash this is the baseline that must be proven
        // already-present before we rewrite history to claim it.
        var assemblyNotApplied = assemblyMigrationIds.Where(id => !appliedSet.Contains(id)).ToList();

        var verified = new List<string>();
        var unverified = new List<string>();
        foreach (var migrationId in assemblyNotApplied)
        {
            if (!migrationsAssembly.Migrations.TryGetValue(migrationId, out var typeInfo))
            {
                unverified.Add(migrationId);
                continue;
            }

            var migration = (Migration)Activator.CreateInstance(typeInfo.AsType())!;
            if (await MigrationSchemaVerifier.IsMigrationApplied(db, migration, migrationId))
                verified.Add(migrationId);
            else
                unverified.Add(migrationId);
        }

        // Fail safe: any unverified baseline migration → leave history untouched, surface the mismatch.
        if (unverified.Count > 0)
            return new Result(false, 0, 0, staleApplied, unverified, "unverified-assembly-migration");

        // All baseline migrations verified present → rewrite history in one transaction.
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

            // 2. Delete the stale rows.
            await db.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = ANY({0})
                """,
                staleApplied.ToArray());

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

        return new Result(true, staleApplied.Count, verified.Count, staleApplied, [], null);
    }
}
