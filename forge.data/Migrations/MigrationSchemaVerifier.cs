using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Forge.Data.Migrations;

/// <summary>
/// Verifies whether a migration's schema changes are already present in the database
/// by inspecting ANSI-standard information_schema views (works on Postgres, MSSQL, MySQL).
/// Used for self-healing when __EFMigrationsHistory is lost but schema/data are intact.
/// </summary>
public static class MigrationSchemaVerifier
{
    /// <summary>
    /// Checks all Up operations in the migration and returns true only if every
    /// verifiable operation's result is present in the current schema.
    /// </summary>
    public static async Task<bool> IsMigrationApplied(DbContext db, Migration migration, string migrationId)
    {
        IReadOnlyList<MigrationOperation> operations;
        try
        {
            operations = migration.UpOperations;
        }
        catch
        {
            return false;
        }

        if (operations.Count == 0)
            return true;

        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();

        try
        {
            foreach (var op in operations)
            {
                if (!await VerifyOperation(conn, op, migrationId))
                    return false;
            }
            return true;
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }

    private static async Task<bool> VerifyOperation(DbConnection conn, MigrationOperation op, string migrationId)
    {
        try
        {
            return op switch
            {
                CreateTableOperation cto => await TableExists(conn, cto.Name, cto.Schema),
                DropTableOperation dto => !await TableExists(conn, dto.Name, dto.Schema),
                AddColumnOperation aco => await ColumnExists(conn, aco.Table, aco.Name, aco.Schema),
                DropColumnOperation dco => !await ColumnExists(conn, dco.Table, dco.Name, dco.Schema),
                RenameTableOperation rto => await TableExists(conn, rto.NewName ?? rto.Name, rto.Schema),
                RenameIndexOperation rio => await IndexExists(conn, rio.NewName),
                CreateIndexOperation cio => await IndexExists(conn, cio.Name),
                DropIndexOperation dio => !await IndexExists(conn, dio.Name),
                AddForeignKeyOperation afk => await ConstraintExists(conn, afk.Name, afk.Schema),
                DropForeignKeyOperation dfk => !await ConstraintExists(conn, dfk.Name, dfk.Schema),
                AlterDatabaseOperation => true,
                SqlOperation sql => VerifySqlOperation(sql),
                _ => true,
            };
        }
        catch
        {
            return false;
        }
    }

    // ── ANSI information_schema queries (Postgres, MSSQL, MySQL) ──

    // Postgres truncates identifiers to NAMEDATALEN-1 = 63 bytes. A migration may declare a
    // longer constraint/index name (EF emits the full name); the DB stores the 63-char prefix.
    // Match BOTH so existence checks don't false-negative on long names. For names ≤ 63 the two
    // forms are identical, so this is harmless on other providers / short names.
    private static string Trunc(string name) => name.Length > 63 ? name[..63] : name;

    private static async Task<bool> TableExists(DbConnection conn, string table, string? schema = null)
    {
        return await ExistsQuery(conn,
            "SELECT 1 FROM information_schema.tables WHERE table_name IN (@p0, @p1) AND table_schema = @p2",
            table, Trunc(table), schema ?? "public");
    }

    private static async Task<bool> ColumnExists(DbConnection conn, string table, string column, string? schema = null)
    {
        return await ExistsQuery(conn,
            "SELECT 1 FROM information_schema.columns WHERE table_name IN (@p0, @p1) AND column_name IN (@p2, @p3) AND table_schema = @p4",
            table, Trunc(table), column, Trunc(column), schema ?? "public");
    }

    private static async Task<bool> ConstraintExists(DbConnection conn, string constraint, string? schema = null)
    {
        return await ExistsQuery(conn,
            "SELECT 1 FROM information_schema.table_constraints WHERE constraint_name IN (@p0, @p1) AND constraint_schema = @p2",
            constraint, Trunc(constraint), schema ?? "public");
    }

    private static async Task<bool> IndexExists(DbConnection conn, string indexName)
    {
        // information_schema doesn't cover indexes — try Postgres pg_indexes first,
        // then MSSQL sys.indexes, then give up gracefully (assume exists).
        var providerName = conn.GetType().Namespace ?? "";

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return await ExistsQuery(conn,
                "SELECT 1 FROM pg_indexes WHERE indexname IN (@p0, @p1)",
                indexName, Trunc(indexName));
        }

        if (providerName.Contains("SqlClient", StringComparison.OrdinalIgnoreCase))
        {
            return await ExistsQuery(conn,
                "SELECT 1 FROM sys.indexes WHERE name IN (@p0, @p1)",
                indexName, Trunc(indexName));
        }

        // SQLite / other — indexes aren't reliably introspectable, assume applied
        return true;
    }

    /// <summary>
    /// Raw SQL operations are hard to verify generically.
    /// UPDATE/INSERT = data modifications (assume applied if schema is present).
    /// Everything else = assume applied to avoid false negatives.
    /// </summary>
    private static bool VerifySqlOperation(SqlOperation sql)
    {
        // SQL ops are either data modifications (UPDATE, INSERT — already happened or idempotent)
        // or DDL that's covered by other operations in the same migration.
        // We can't generically verify arbitrary SQL, so assume applied.
        return true;
    }

    // ── Query helper ──

    private static async Task<bool> ExistsQuery(DbConnection conn, string sql, params string[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        for (var i = 0; i < parameters.Length; i++)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = $"@p{i}";
            param.Value = parameters[i];
            cmd.Parameters.Add(param);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync();
    }
}
