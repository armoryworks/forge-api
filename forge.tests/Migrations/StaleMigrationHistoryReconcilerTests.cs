using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

using FluentAssertions;

using Forge.Data.Migrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Migrations;

/// <summary>
/// Verifies <see cref="StaleMigrationHistoryReconciler"/> — the boot reconciliation that makes a
/// migration <b>squash</b> safe against an existing-data install (squash plan §3.1).
///
/// <para>Runs against a real Postgres (Testcontainers / <c>FORGE_TEST_PG</c>) because the reconciler
/// uses raw SQL, transactions, and <c>information_schema</c> introspection that the EF Core InMemory
/// provider cannot model.</para>
///
/// <para>The fixture migrates the full (pre-squash) history once, so the live schema matches every
/// migration in the assembly. We drive the reconciler with crafted applied/assembly ID lists — and a
/// synthetic "stale" history row — to exercise each path without actually squashing the test
/// assembly. Mutating tests restore <c>__EFMigrationsHistory</c> and drop the backup table afterward
/// so the shared fixture is order-independent.</para>
///
/// <para><b>Why we don't use the historical InitialCreate as the "baseline":</b> the real
/// <c>InitialBaseline</c> is generated from the current model, so every object it declares exists.
/// The April-2026 <c>InitialCreate</c> created 111 tables, 70+ later migrations renamed/dropped some,
/// so verifying it against today's schema correctly fails — that's a faithful-simulation problem, not
/// a reconciler bug. We instead use the most recent migration (whose objects nothing later mutated)
/// to exercise the verify-and-insert path.</para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StaleMigrationHistoryReconcilerTests(PostgresFixture fixture)
{
    private const string ProductVersion = "10.0.9";
    private const string FakeStaleId = "00000000000000_OldSquashedChain";
    private const string BackupTable = "__EFMigrationsHistory_pre_squash";

    private static IMigrationsAssembly MigrationsAssembly(DbContext db) =>
        ((IInfrastructure<IServiceProvider>)db).Instance.GetRequiredService<IMigrationsAssembly>();

    // ── raw history helpers (the reconciler operates on the real table) ──

    private static async Task<List<string>> HistoryRowsAsync(DbContext db)
    {
        var rows = new List<string>();
        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" """;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        }
        finally { if (!wasOpen) await conn.CloseAsync(); }
        return rows;
    }

    private static Task InsertHistoryRowAsync(DbContext db, string migrationId) =>
        db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ({0}, {1}) ON CONFLICT DO NOTHING""",
            migrationId, ProductVersion);

    private static Task DeleteHistoryRowAsync(DbContext db, string migrationId) =>
        db.Database.ExecuteSqlRawAsync("""DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = {0}""", migrationId);

    private static Task DropBackupTableAsync(DbContext db) =>
        db.Database.ExecuteSqlRawAsync($"""DROP TABLE IF EXISTS "{BackupTable}" """);

    private static async Task<bool> TableExistsAsync(DbContext db, string table)
    {
        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM information_schema.tables WHERE table_name = @t AND table_schema = 'public'";
            var p = cmd.CreateParameter(); p.ParameterName = "@t"; p.Value = table; cmd.Parameters.Add(p);
            await using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync();
        }
        finally { if (!wasOpen) await conn.CloseAsync(); }
    }

    // ── tests ──

    /// <summary>No stale rows → no-op, returns the signature-absent reason, touches nothing.</summary>
    [Fact]
    public async Task NoStaleHistory_IsNoOp()
    {
        await using var db = fixture.CreateContext();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var assembly = db.Database.GetMigrations().ToList(); // identical set → no stale IDs

        var result = await StaleMigrationHistoryReconciler.ReconcileAsync(
            db, applied, assembly, MigrationsAssembly(db), ProductVersion);

        result.Reconciled.Should().BeFalse();
        result.AbortReason.Should().Be("no-stale-history");
        result.StaleRemoved.Should().Be(0);
    }

    /// <summary>
    /// Stale history with nothing to re-insert (the baseline is already in history) → backs up,
    /// deletes the stale row, reconciles. Exercises the signature → backup → delete → commit path.
    /// </summary>
    [Fact]
    public async Task StaleHistory_DeletesStaleRow_Reconciles()
    {
        await using var db = fixture.CreateContext();
        var real = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        await InsertHistoryRowAsync(db, FakeStaleId);

        try
        {
            // Assembly knows only the real set; history additionally holds the stale chain ID.
            var result = await StaleMigrationHistoryReconciler.ReconcileAsync(
                db, [.. real, FakeStaleId], real, MigrationsAssembly(db), ProductVersion);

            result.Reconciled.Should().BeTrue();
            result.AbortReason.Should().BeNull();
            result.StaleRemoved.Should().Be(1);
            result.BaselineInserted.Should().Be(0);

            var after = await HistoryRowsAsync(db);
            after.Should().NotContain(FakeStaleId);
            after.Should().BeEquivalentTo(real); // back to exactly the real set
            (await TableExistsAsync(db, BackupTable)).Should().BeTrue();
        }
        finally
        {
            await DeleteHistoryRowAsync(db, FakeStaleId);
            await DropBackupTableAsync(db);
        }
    }

    /// <summary>
    /// Stale history AND a baseline migration absent from history but present in the schema → verifies
    /// it present, then deletes the stale row and re-inserts the baseline. Exercises the
    /// verify → insert path using the most recent migration (its objects were not later mutated).
    /// </summary>
    [Fact]
    public async Task StaleHistory_VerifiesAndReInsertsBaseline()
    {
        await using var db = fixture.CreateContext();
        var real = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var baselineId = real.Last(); // latest migration — cleanly verifiable

        // Remove the baseline from history (present in schema, absent from history) + add a stale row.
        await DeleteHistoryRowAsync(db, baselineId);
        await InsertHistoryRowAsync(db, FakeStaleId);

        try
        {
            var applied = real.Take(real.Count - 1).Append(FakeStaleId).ToList(); // history now: real-minus-last + stale
            var result = await StaleMigrationHistoryReconciler.ReconcileAsync(
                db, applied, real, MigrationsAssembly(db), ProductVersion);

            result.Reconciled.Should().BeTrue();
            result.AbortReason.Should().BeNull();
            result.StaleRemoved.Should().Be(1);
            result.BaselineInserted.Should().Be(1);

            var after = await HistoryRowsAsync(db);
            after.Should().Contain(baselineId);
            after.Should().NotContain(FakeStaleId);
            after.Should().BeEquivalentTo(real);
        }
        finally
        {
            await InsertHistoryRowAsync(db, baselineId); // ensure restored even if assertions failed
            await DeleteHistoryRowAsync(db, FakeStaleId);
            await DropBackupTableAsync(db);
        }
    }

    /// <summary>
    /// Stale history but the BASELINE (first assembly migration) cannot be verified present →
    /// FAIL SAFE: history is left untouched, no backup table is created, the reason is surfaced.
    /// </summary>
    [Fact]
    public async Task StaleHistory_BaselineNotPresent_AbortsAndLeavesHistoryUntouched()
    {
        await using var db = fixture.CreateContext();
        var before = await HistoryRowsAsync(db);

        // The sole assembly migration (the "baseline") does not exist in the assembly → unverifiable,
        // so the baseline is not present and reconciliation must abort.
        var bogusBaseline = new List<string> { "00000000000000_DoesNotExist" };

        var result = await StaleMigrationHistoryReconciler.ReconcileAsync(
            db, before, bogusBaseline, MigrationsAssembly(db), ProductVersion);

        result.Reconciled.Should().BeFalse();
        result.AbortReason.Should().Be("baseline-not-present");
        result.UnverifiedAssemblyMigrations.Should().Contain("00000000000000_DoesNotExist");

        (await HistoryRowsAsync(db)).Should().BeEquivalentTo(before); // unchanged
        (await TableExistsAsync(db, BackupTable)).Should().BeFalse(); // abort happens before backup
    }

    /// <summary>
    /// The post-squash reality: stale history, a verified-present baseline, AND a genuinely-new
    /// migration that is NOT yet applied (its schema change isn't present). The reconciler must
    /// reconcile to the baseline and leave the new migration PENDING (out of history) so the normal
    /// MigrateAsync that follows applies it — rather than aborting. Mirrors the DropDeadOutboxColumns
    /// case on a legacy install.
    /// </summary>
    [Fact]
    public async Task StaleHistory_BaselinePresent_LeavesGenuinelyNewMigrationPending()
    {
        await using var db = fixture.CreateContext();
        var realBaseline = db.Database.GetMigrations().First(); // InitialBaseline — verifies present
        const string futureId = "99999999999999_NotYetApplied"; // unknown to assembly → cannot verify → pending

        // History holds only a stale row; baseline is absent from history (must be verified present).
        var applied = new List<string> { FakeStaleId };
        var assembly = new List<string> { realBaseline, futureId };

        try
        {
            var result = await StaleMigrationHistoryReconciler.ReconcileAsync(
                db, applied, assembly, MigrationsAssembly(db), ProductVersion);

            result.Reconciled.Should().BeTrue();
            result.AbortReason.Should().BeNull();
            result.BaselineInserted.Should().Be(1);                 // baseline verified + inserted
            result.PendingMigrations.Should().ContainSingle()
                .Which.Should().Be(futureId);                       // new migration left for MigrateAsync
            (await HistoryRowsAsync(db)).Should().Contain(realBaseline);
            (await HistoryRowsAsync(db)).Should().NotContain(futureId);
        }
        finally
        {
            await DropBackupTableAsync(db);
        }
    }
}
