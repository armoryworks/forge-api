using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Npgsql;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// In-app port of forge-db's <c>dump</c>/<c>import</c> verbs (forge-db docs/DESIGN.md §6.2),
/// packaged as a zip so the browser can carry it. The archive layout is the forge-db dump
/// directory, zipped — <c>manifest.json</c> + <c>tables/schema.table.copy</c> (COPY text) — so a
/// UI dump can be unzipped and fed to the CLI and vice versa.
///
/// <para><b>Dump</b> is read-only: every application table streams out via <c>COPY … TO STDOUT</c>
/// with an explicit column list (generated columns omitted — they can't be COPYed back).
/// Infrastructure that re-creates itself is excluded: the <c>hangfire</c>/<c>forge_db</c> schemas
/// and EF's history table.</para>
///
/// <para><b>Import</b> is the clean-rebuild load: one transaction TRUNCATEs the selected tables
/// (<c>RESTART IDENTITY CASCADE</c>) and COPYs the dump back, loading the intersection of dumped
/// and current columns so modest schema drift is tolerated. FK triggers are suspended for the load
/// (<c>session_replication_role = replica</c> — requires a superuser connection, which the
/// self-host stack's default <c>postgres</c> user is), so a post-load pass re-validates every FK
/// and reports orphans; optional purge of soft-deleted rows (<c>deleted_at IS NOT NULL</c>) runs
/// first. Sequences are bumped past <c>max(id)</c> and <c>ANALYZE</c> refreshes stats.</para>
/// </summary>
public class DatabaseTransferService(
    IConfiguration config,
    ILogger<DatabaseTransferService> logger) : IDatabaseTransferService
{
    private static readonly string[] ExcludedSchemas =
        ["pg_catalog", "information_schema", "pg_toast", "hangfire", "forge_db"];

    private const string EfHistoryTable = "__EFMigrationsHistory";
    private const string ManifestEntry = "manifest.json";

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private string ConnectionString =>
        config.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured");

    // ── Summary ──────────────────────────────────────────────────────────────────────────────────

    public async Task<DatabaseTransferSummaryModel> GetSummaryAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);

        var tables = new List<DumpTableInfoModel>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT n.nspname, c.relname,
                   GREATEST(c.reltuples, 0)::bigint,
                   pg_total_relation_size(c.oid)
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'r' AND n.nspname <> ALL (@excluded) AND c.relname <> @efHistory
            ORDER BY pg_total_relation_size(c.oid) DESC, n.nspname, c.relname
            """, conn))
        {
            cmd.Parameters.AddWithValue("excluded", ExcludedSchemas);
            cmd.Parameters.AddWithValue("efHistory", EfHistoryTable);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                tables.Add(new DumpTableInfoModel(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3)));
        }

        return new DatabaseTransferSummaryModel(
            conn.Database ?? "?",
            tables.Count,
            tables.Sum(t => t.EstimatedRows),
            tables.Sum(t => t.SizeBytes),
            tables);
    }

    // ── Dump ─────────────────────────────────────────────────────────────────────────────────────

    public async Task WriteDumpZipAsync(Stream output, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);

        var tables = await DiscoverTablesAsync(conn, ct);
        logger.LogInformation("Database dump started: {TableCount} tables", tables.Count);

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var entries = new List<ManifestTable>();
        foreach (var t in tables)
        {
            ct.ThrowIfCancellationRequested();
            entries.Add(await DumpTableAsync(conn, zip, t, ct));
        }

        var manifest = new Manifest(
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            conn.Host ?? "?",
            conn.Database ?? "?",
            SchemaFingerprint: null, // the CLI stamps this from the forge-db schema/ tree; the app has no repo
            entries);
        var manifestEntry = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
        await using (var s = manifestEntry.Open())
            await JsonSerializer.SerializeAsync(s, manifest, ManifestJson, ct);

        logger.LogInformation("Database dump completed: {TableCount} tables, {Rows} rows",
            entries.Count, entries.Sum(e => e.Rows));
    }

    private static async Task<ManifestTable> DumpTableAsync(
        NpgsqlConnection conn, ZipArchive zip, TableRef t, CancellationToken ct)
    {
        var relFile = $"tables/{t.Schema}.{t.Name}.copy";
        var colList = string.Join(", ", t.Columns.Select(QuoteIdent));

        long rows = 0, bytes = 0;
        using var sha = SHA256.Create();
        var entry = zip.CreateEntry(relFile, CompressionLevel.Fastest);
        await using (var entryStream = entry.Open())
        {
            using var reader = await conn.BeginTextExportAsync(
                $"COPY {Qualify(t.Schema, t.Name)} ({colList}) TO STDOUT", ct);
            var buf = new char[64 * 1024];
            int n;
            while ((n = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
            {
                var chunk = Encoding.UTF8.GetBytes(buf, 0, n);
                await entryStream.WriteAsync(chunk, ct);
                sha.TransformBlock(chunk, 0, chunk.Length, null, 0);
                bytes += chunk.Length;
                for (var i = 0; i < n; i++)
                    if (buf[i] == '\n')
                        rows++;
            }
        }
        sha.TransformFinalBlock([], 0, 0);
        return new ManifestTable(t.Schema, t.Name, t.Columns, rows, bytes,
            Convert.ToHexString(sha.Hash!).ToLowerInvariant(), relFile);
    }

    // ── Import ───────────────────────────────────────────────────────────────────────────────────

    public async Task<DatabaseImportReportModel> ImportZipAsync(
        Stream zipFile, DatabaseImportOptionsModel options, CancellationToken ct = default)
    {
        // ZipArchive needs a seekable stream; uploads aren't. Spool to a temp file.
        var tempPath = Path.Combine(Path.GetTempPath(), $"forge-import-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var temp = File.Create(tempPath))
                await zipFile.CopyToAsync(temp, ct);

            using var zip = ZipFile.OpenRead(tempPath);
            var manifest = ReadManifest(zip);

            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync(ct);

            var targetCols = (await DiscoverTablesAsync(conn, ct))
                .ToDictionary(t => $"{t.Schema}.{t.Name}", t => t.Columns.ToHashSet(StringComparer.Ordinal));

            // ── Select: manifest minus excludes minus tables the schema no longer has ────────────
            var excluded = new List<string>();
            var missing = new List<string>();
            var selected = new List<(ManifestTable Entry, List<string> Cols, List<string> Dropped)>();
            foreach (var t in manifest.Tables)
            {
                var qualified = $"{t.Schema}.{t.Name}";
                if (MatchesAny(options.ExcludePatterns, t.Schema, t.Name)) { excluded.Add(qualified); continue; }
                if (!targetCols.TryGetValue(qualified, out var current)) { missing.Add(qualified); continue; }
                var cols = t.Columns.Where(current.Contains).ToList();
                if (cols.Count == 0) { missing.Add(qualified); continue; }
                selected.Add((t, cols, t.Columns.Where(c => !current.Contains(c)).ToList()));
            }

            logger.LogInformation(
                "Database import started: {Selected} tables selected, {Excluded} excluded, {Missing} missing",
                selected.Count, excluded.Count, missing.Count);

            // FK triggers off for the load: dump order is arbitrary and excluded tables may be FK
            // parents. Orphans this can create are re-checked below.
            await SetReplicationRoleAsync(conn, "replica", ct);

            var loaded = new List<ImportedTableResultModel>();
            await using (var tx = await conn.BeginTransactionAsync(ct))
            {
                if (selected.Count > 0)
                {
                    var truncateList = string.Join(", ", selected.Select(s => Qualify(s.Entry.Schema, s.Entry.Name)));
                    await using var cmd = new NpgsqlCommand(
                        $"TRUNCATE {truncateList} RESTART IDENTITY CASCADE", conn, tx)
                        { CommandTimeout = 600 };
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                foreach (var (entry, cols, dropped) in selected)
                {
                    ct.ThrowIfCancellationRequested();
                    await LoadTableAsync(conn, zip, entry, cols, dropped, ct);
                    loaded.Add(new ImportedTableResultModel($"{entry.Schema}.{entry.Name}", entry.Rows, dropped));
                }
                await tx.CommitAsync(ct);
            }

            await SetReplicationRoleAsync(conn, "origin", ct);

            await FixSequencesAsync(conn, selected, ct);

            long purged = 0;
            if (options.PurgeSoftDeleted)
                purged = await PurgeSoftDeletedAsync(conn, selected, ct);

            var orphans = await FindFkOrphansAsync(
                conn, selected.Select(s => $"{s.Entry.Schema}.{s.Entry.Name}").ToHashSet(StringComparer.Ordinal), ct);

            await using (var analyze = new NpgsqlCommand("ANALYZE", conn) { CommandTimeout = 600 })
                await analyze.ExecuteNonQueryAsync(ct);

            var success = orphans.Count == 0 || options.AllowFkOrphans;
            logger.LogInformation(
                "Database import finished: {Loaded} tables, {Rows} rows, {Purged} soft-deleted purged, {Orphans} FK orphan constraint(s), success={Success}",
                loaded.Count, loaded.Sum(t => t.Rows), purged, orphans.Count, success);

            return new DatabaseImportReportModel(success, loaded, excluded, missing, purged, orphans);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    private static Manifest ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry(ManifestEntry)
            ?? throw new InvalidDataException("not a forge dump archive (no manifest.json at the zip root)");
        using var s = entry.Open();
        return JsonSerializer.Deserialize<Manifest>(s, ManifestJson)
            ?? throw new InvalidDataException("could not parse manifest.json");
    }

    private static async Task LoadTableAsync(
        NpgsqlConnection conn, ZipArchive zip, ManifestTable entry,
        List<string> cols, List<string> dropped, CancellationToken ct)
    {
        var file = zip.GetEntry(entry.File)
            ?? throw new InvalidDataException($"dump file listed in the manifest is missing: {entry.File}");
        var colList = string.Join(", ", cols.Select(QuoteIdent));

        await using var writer = await conn.BeginTextImportAsync(
            $"COPY {Qualify(entry.Schema, entry.Name)} ({colList}) FROM STDIN", ct);
        using var raw = new StreamReader(file.Open(), Encoding.UTF8);

        if (dropped.Count == 0)
        {
            var buf = new char[64 * 1024];
            int n;
            while ((n = await raw.ReadAsync(buf, 0, buf.Length)) > 0)
                await writer.WriteAsync(buf.AsMemory(0, n), ct);
            return;
        }

        // The current schema lost columns since the dump: re-project each COPY text row onto the
        // surviving columns. In-value tabs arrive as the two-character escape \t, never a literal
        // tab, so splitting on literal tabs is exact.
        var keep = entry.Columns.Select((c, i) => (c, i)).Where(x => cols.Contains(x.c)).Select(x => x.i).ToArray();
        while (await raw.ReadLineAsync(ct) is { } line)
        {
            var fields = SplitCopyLine(line, entry.Columns.Count);
            await writer.WriteAsync((string.Join('\t', keep.Select(i => fields[i])) + "\n").AsMemory(), ct);
        }
    }

    internal static string[] SplitCopyLine(string line, int expectedFields)
    {
        var fields = new string[expectedFields];
        var idx = 0;
        var start = 0;
        for (var i = 0; i < line.Length && idx < expectedFields - 1; i++)
        {
            if (line[i] == '\t')
            {
                fields[idx++] = line[start..i];
                start = i + 1;
            }
        }
        fields[idx] = line[start..];
        return fields;
    }

    private static async Task SetReplicationRoleAsync(NpgsqlConnection conn, string role, CancellationToken ct)
    {
        try
        {
            await using var cmd = new NpgsqlCommand($"SET session_replication_role = {role}", conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "42501")
        {
            throw new InvalidOperationException(
                "Import needs to suspend FK triggers for the load, which requires the database " +
                "connection to be a superuser. The default self-host stack's postgres user qualifies; " +
                "a hardened deployment needs to run the import via the forge-db CLI instead.", ex);
        }
    }

    /// <summary>TRUNCATE reset every serial/identity sequence and COPY does not advance them —
    /// bump each past the loaded data.</summary>
    private static async Task FixSequencesAsync(
        NpgsqlConnection conn, List<(ManifestTable Entry, List<string> Cols, List<string> Dropped)> selected,
        CancellationToken ct)
    {
        foreach (var (entry, cols, _) in selected)
        {
            var qualified = Qualify(entry.Schema, entry.Name);
            foreach (var col in cols)
            {
                string? seq;
                await using (var find = new NpgsqlCommand("SELECT pg_get_serial_sequence(@tbl, @col)", conn))
                {
                    find.Parameters.AddWithValue("tbl", qualified);
                    find.Parameters.AddWithValue("col", col);
                    seq = await find.ExecuteScalarAsync(ct) as string;
                }
                if (seq is null) continue;

                // Identifiers come straight from the catalog and are quote-escaped — not user input.
                await using var set = new NpgsqlCommand(
                    $"SELECT setval(@seq, GREATEST(COALESCE(max({QuoteIdent(col)}), 0), 1), " +
                    $"max({QuoteIdent(col)}) IS NOT NULL) FROM {qualified}", conn);
                set.Parameters.AddWithValue("seq", seq);
                await set.ExecuteScalarAsync(ct);
            }
        }
    }

    /// <summary>Forge soft-deletes everywhere (<c>deleted_at</c>); a clean rebuild is the natural
    /// moment to let the tombstones go. Children delete before parents is not guaranteed — any
    /// stragglers surface in the FK orphan report.</summary>
    private static async Task<long> PurgeSoftDeletedAsync(
        NpgsqlConnection conn, List<(ManifestTable Entry, List<string> Cols, List<string> Dropped)> selected,
        CancellationToken ct)
    {
        long total = 0;
        foreach (var (entry, cols, _) in selected)
        {
            if (!cols.Contains("deleted_at")) continue;
            await using var cmd = new NpgsqlCommand(
                $"DELETE FROM {Qualify(entry.Schema, entry.Name)} WHERE deleted_at IS NOT NULL", conn)
                { CommandTimeout = 600 };
            total += await cmd.ExecuteNonQueryAsync(ct);
        }
        return total;
    }

    /// <summary>Re-check every FK touching a loaded table — the load ran with FK triggers
    /// suspended, so nothing enforced them.</summary>
    private static async Task<List<FkOrphanInfoModel>> FindFkOrphansAsync(
        NpgsqlConnection conn, HashSet<string> loadedTables, CancellationToken ct)
    {
        var fks = new List<(string Name, string CSchema, string CTable, string PSchema, string PTable, string[] CCols, string[] PCols)>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT con.conname,
                   cn.nspname, cc.relname,
                   pn.nspname, pc.relname,
                   ARRAY(SELECT a.attname FROM unnest(con.conkey) WITH ORDINALITY k(attnum, ord)
                         JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum
                         ORDER BY k.ord),
                   ARRAY(SELECT a.attname FROM unnest(con.confkey) WITH ORDINALITY k(attnum, ord)
                         JOIN pg_attribute a ON a.attrelid = con.confrelid AND a.attnum = k.attnum
                         ORDER BY k.ord)
            FROM pg_constraint con
            JOIN pg_class cc ON cc.oid = con.conrelid
            JOIN pg_namespace cn ON cn.oid = cc.relnamespace
            JOIN pg_class pc ON pc.oid = con.confrelid
            JOIN pg_namespace pn ON pn.oid = pc.relnamespace
            WHERE con.contype = 'f'
            """, conn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
                fks.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                    r.GetFieldValue<string[]>(5), r.GetFieldValue<string[]>(6)));
        }

        var violations = new List<FkOrphanInfoModel>();
        foreach (var fk in fks)
        {
            if (!loadedTables.Contains($"{fk.CSchema}.{fk.CTable}")) continue;

            var child = Qualify(fk.CSchema, fk.CTable);
            var parent = Qualify(fk.PSchema, fk.PTable);
            var notNull = string.Join(" AND ", fk.CCols.Select(c => $"c.{QuoteIdent(c)} IS NOT NULL"));
            var join = string.Join(" AND ", fk.CCols.Zip(fk.PCols,
                (cc, pc) => $"p.{QuoteIdent(pc)} = c.{QuoteIdent(cc)}"));
            await using var cmd = new NpgsqlCommand(
                $"SELECT count(*) FROM {child} c WHERE {notNull} AND NOT EXISTS (SELECT 1 FROM {parent} p WHERE {join})",
                conn)
                { CommandTimeout = 600 };
            var count = (long)(await cmd.ExecuteScalarAsync(ct))!;
            if (count > 0)
                violations.Add(new FkOrphanInfoModel(
                    fk.Name, $"{fk.CSchema}.{fk.CTable}", $"{fk.PSchema}.{fk.PTable}", count));
        }
        return violations;
    }

    // ── Shared plumbing ──────────────────────────────────────────────────────────────────────────

    private sealed record TableRef(string Schema, string Name, List<string> Columns);

    /// <summary>Serialization shape matches forge-db's manifest exactly (camelCase) so archives are
    /// interchangeable between the CLI and the app.</summary>
    private sealed record Manifest(
        string DumpedAtUtc, string SourceHost, string SourceDatabase, string? SchemaFingerprint,
        List<ManifestTable> Tables);

    private sealed record ManifestTable(
        string Schema, string Name, List<string> Columns, long Rows, long Bytes, string Sha256, string File);

    private static async Task<List<TableRef>> DiscoverTablesAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var result = new List<TableRef>();
        await using var cmd = new NpgsqlCommand("""
            SELECT n.nspname, c.relname, a.attname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_attribute a ON a.attrelid = c.oid
            WHERE c.relkind = 'r'
              AND n.nspname <> ALL (@excluded)
              AND c.relname <> @efHistory
              AND a.attnum > 0 AND NOT a.attisdropped AND a.attgenerated = ''
            ORDER BY n.nspname, c.relname, a.attnum
            """, conn);
        cmd.Parameters.AddWithValue("excluded", ExcludedSchemas);
        cmd.Parameters.AddWithValue("efHistory", EfHistoryTable);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        string? curSchema = null, curTable = null;
        var cols = new List<string>();
        while (await r.ReadAsync(ct))
        {
            var (schema, table, col) = (r.GetString(0), r.GetString(1), r.GetString(2));
            if (schema != curSchema || table != curTable)
            {
                if (curTable is not null) result.Add(new TableRef(curSchema!, curTable, cols));
                (curSchema, curTable, cols) = (schema, table, []);
            }
            cols.Add(col);
        }
        if (curTable is not null) result.Add(new TableRef(curSchema!, curTable, cols));
        return result;
    }

    internal static bool MatchesAny(List<string> patterns, string schema, string table) =>
        patterns.Any(p => Matches(p, schema, table));

    internal static bool Matches(string pattern, string schema, string table)
    {
        var re = new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return re.IsMatch($"{schema}.{table}") || (!pattern.Contains('.') && re.IsMatch(table));
    }

    private static string QuoteIdent(string ident) => $"\"{ident.Replace("\"", "\"\"")}\"";
    private static string Qualify(string schema, string table) => $"{QuoteIdent(schema)}.{QuoteIdent(table)}";
}
