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
/// uses raw SQL and <c>information_schema</c> introspection that the EF Core InMemory provider cannot
/// model — InMemory ignores transactions and has no <c>information_schema</c>.</para>
///
/// <para>The fixture migrates the full (pre-squash) history once, so the live schema matches every
/// migration in the assembly. We then drive the reconciler with crafted applied/assembly ID lists to
/// simulate the post-squash state without actually squashing the test assembly.</para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StaleMigrationHistoryReconcilerTests(PostgresFixture fixture)
{
    private const string ProductVersion = "10.0.9";

    private static IMigrationsAssembly MigrationsAssembly(DbContext db) =>
        ((IInfrastructure<IServiceProvider>)db).Instance.GetRequiredService<IMigrationsAssembly>();

    private static async Task<List<string>> HistoryRowsAsync(DbContext db)
    {
        var rows = new List<string>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" """;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        }
        finally { await conn.CloseAsync(); }
        return rows;
    }

    private static async Task<bool> TableExistsAsync(DbContext db, string table)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM information_schema.tables WHERE table_name = @t AND table_schema = 'public'";
            var p = cmd.CreateParameter(); p.ParameterName = "@t"; p.Value = table; cmd.Parameters.Add(p);
            await using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync();
        }
        finally { await conn.CloseAsync(); }
    }

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
    /// Stale history + a present-and-verifiable baseline → history is rewritten to the baseline, the
    /// pre-squash backup is created, and nothing in the live schema is touched.
    /// Simulates post-squash by treating the first real migration as the sole "baseline" and every
    /// other applied ID as stale.
    /// </summary>
    [Fact]
    public async Task StaleHistory_BaselinePresent_Reconciles()
    {
        await using var db = fixture.CreateContext();
        var allReal = db.Database.GetMigrations().ToList();
        var baselineId = allReal.First(); // InitialCreate — its objects exist in the migrated schema

        // Simulate the squashed assembly: it knows ONLY the baseline.
        var assemblyIds = new List<string> { baselineId };
        // Applied (per history) = everything EXCEPT the baseline, so the baseline is "not applied" and
        // must be verified-present and re-inserted; the rest are stale and must be removed.
        var appliedIds = allReal.Skip(1).ToList();

        var result = await StaleMigrationHistoryReconciler.ReconcileAsync(
            db, appliedIds, assemblyIds, MigrationsAssembly(db), ProductVersion);

        result.Reconciled.Should().BeTrue();
        result.AbortReason.Should().BeNull();
        result.StaleRemoved.Should().Be(appliedIds.Count);
        result.BaselineInserted.Should().Be(1);

        // Backup table exists; live schema (e.g. the jobs table) is untouched.
        (await TableExistsAsync(db, "__EFMigrationsHistory_pre_squash")).Should().BeTrue();
        (await TableExistsAsync(db, "jobs")).Should().BeTrue();
    }

    /// <summary>
    /// Stale history but a baseline migration the assembly cannot instantiate (cannot be verified) →
    /// FAIL SAFE: history is left untouched, no backup table is created, the unverified ID is surfaced.
    /// </summary>
    [Fact]
    public async Task StaleHistory_UnverifiableBaseline_AbortsAndLeavesHistoryUntouched()
    {
        await using var db = fixture.CreateContext();
        var before = await HistoryRowsAsync(db);

        // Assembly claims a migration that does not exist in the migrations assembly → unverifiable.
        var assemblyIds = new List<string> { "00000000000000_DoesNotExist" };
        var appliedIds = before; // all real IDs are "stale" relative to the bogus assembly

        var result = await StaleMigrationHistoryReconciler.ReconcileAsync(
            db, appliedIds, assemblyIds, MigrationsAssembly(db), ProductVersion);

        result.Reconciled.Should().BeFalse();
        result.AbortReason.Should().Be("unverified-assembly-migration");
        result.UnverifiedAssemblyMigrations.Should().Contain("00000000000000_DoesNotExist");

        // History unchanged; no destructive rewrite occurred.
        (await HistoryRowsAsync(db)).Should().BeEquivalentTo(before);
    }
}
