using System.Data;
using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Npgsql;

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
    /// <param name="connectionString">
    /// The configured connection string (with credentials intact). Passed explicitly rather
    /// than read from the live <see cref="AppDbContext"/> connection, whose password Npgsql
    /// strips after the connection is first opened (PersistSecurityInfo=false). Needed to
    /// connect to the <c>postgres</c> maintenance database when the target must be created.
    /// </param>
    public static async Task<bool> EnsureSchemaAsync(AppDbContext db, string connectionString, CancellationToken cancellationToken = default)
    {
        // The schema apply connects to the target database, so it must exist first.
        // Normally the Postgres container creates POSTGRES_DB on first init, but a
        // RECREATE_DB wipe drops it with nothing to recreate it — so ensure it here.
        await EnsureDatabaseAsync(connectionString, cancellationToken);

        if (await SchemaExistsAsync(db, cancellationToken))
            return false;

        var sql = LoadSchemaSql();
        // Parameterless raw batch → Npgsql simple-query protocol → the multi-statement DDL
        // (dollar-quoted function bodies included) executes as one batch.
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        return true;
    }

    /// <summary>
    /// Ensures the target database itself exists, creating an empty one if missing.
    /// Returns true when it created the database, false when it already existed.
    /// Connects to the <c>postgres</c> maintenance database to do so, since the
    /// target may not exist yet (e.g. just after a RECREATE_DB drop).
    /// </summary>
    public static async Task<bool> EnsureDatabaseAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var targetDatabase = new NpgsqlConnectionStringBuilder(connectionString).Database
            ?? throw new InvalidOperationException("The connection string does not specify a Database.");

        // Talk to the always-present maintenance database to check/create the target.
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
            Pooling = false,
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name;";
            var param = check.CreateParameter();
            param.ParameterName = "name";
            param.Value = targetDatabase;
            check.Parameters.Add(param);
            if (await check.ExecuteScalarAsync(cancellationToken) is not null)
                return false; // Already exists — nothing to do.
        }

        // Database identifiers can't be parameterized; the name comes from our own
        // configured connection string (trusted). Double-quote, escaping any quotes.
        var quoted = targetDatabase.Replace("\"", "\"\"");
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{quoted}\";";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }
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
