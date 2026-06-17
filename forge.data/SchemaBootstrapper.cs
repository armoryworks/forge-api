using System.Data;
using System.Reflection;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Data;

/// <summary>
/// Applies the declarative schema owned by the <c>forge-db</c> project.
/// <para>
/// Schema ownership moved off EF Core migrations (retired 2026-06-17): the canonical
/// desired state lives in forge-db's <c>schema/</c> tree and is assembled into
/// <c>forge.data/Schema/forge-schema.sql</c> (one ordered DDL file: extension → tables →
/// FKs → indexes → functions → triggers — including the things EF's model can't express,
/// e.g. the pgvector extension and the acct_journal immutability triggers).
/// </para>
/// <para>
/// On a <b>fresh</b> database this applies the full schema; on an <b>existing</b> database
/// (every current install) it is a no-op. Detection uses a sentinel core table so it never
/// re-runs against a populated DB. Regenerate the SQL with
/// <c>forge-db assemble --repo &lt;forge-db&gt; --out forge.data/Schema/forge-schema.sql</c>.
/// </para>
/// </summary>
public static class SchemaBootstrapper
{
    // A core table that exists iff the schema has been applied. Cheap, deterministic probe.
    private const string SentinelTable = "public.jobs";

    private const string ResourceSuffix = "forge-schema.sql";

    /// <summary>
    /// Ensures the declarative schema is present. Returns true when it created the schema
    /// (fresh DB), false when the schema already existed (no-op).
    /// </summary>
    public static async Task<bool> EnsureSchemaAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (await SchemaExistsAsync(db, cancellationToken))
            return false;

        var sql = LoadSchemaSql();
        // Parameterless raw batch → Npgsql simple-query protocol → the multi-statement DDL
        // (dollar-quoted function bodies included) executes as one batch.
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        return true;
    }

    private static async Task<bool> SchemaExistsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT to_regclass('{SentinelTable}') IS NOT NULL;";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is bool exists && exists;
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }

    private static string LoadSchemaSql()
    {
        var assembly = typeof(SchemaBootstrapper).Assembly;
        var resourceName = Array.Find(
            assembly.GetManifestResourceNames(),
            n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Embedded schema resource '*{ResourceSuffix}' not found in {assembly.GetName().Name}.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
