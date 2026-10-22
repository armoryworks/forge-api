using Microsoft.EntityFrameworkCore;
using Npgsql;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class SearchRepository(AppDbContext db) : ISearchRepository
{
    public async Task<List<SearchResultModel>> SearchAsync(string term, int limit, CancellationToken ct)
    {
        var perEntity = Math.Max(limit / 4, 3);
        var results = new List<SearchResultModel>();

        var sql = """
            SELECT entity_type, entity_id, title, subtitle, icon, url
            FROM (
                (SELECT 'Job' AS entity_type, id AS entity_id, job_number AS title,
                        title AS subtitle, 'work' AS icon, '/kanban' AS url,
                        CASE WHEN lower(job_number) = lower(@term) THEN 0
                             WHEN job_number ILIKE @prefix ESCAPE '\' THEN 1
                             WHEN job_number ILIKE @pattern ESCAPE '\' THEN 2
                             ELSE 3 END AS match_rank,
                        1 AS entity_order, updated_at
                 FROM jobs
                 WHERE deleted_at IS NULL AND is_archived = false
                   AND (job_number ILIKE @pattern ESCAPE '\' OR title ILIKE @pattern ESCAPE '\')
                 ORDER BY match_rank, length(job_number), updated_at DESC LIMIT @per)
                UNION ALL
                (SELECT 'Customer', id, name AS title,
                        NULLIF(company_name, name) AS subtitle, 'people' AS icon, '/customers' AS url,
                        CASE WHEN lower(name) = lower(@term) THEN 0
                             WHEN name ILIKE @prefix ESCAPE '\' THEN 1
                             WHEN name ILIKE @pattern ESCAPE '\' THEN 2
                             ELSE 3 END AS match_rank,
                        2 AS entity_order, updated_at
                 FROM customers
                 WHERE deleted_at IS NULL
                   AND (name ILIKE @pattern ESCAPE '\' OR company_name ILIKE @pattern ESCAPE '\')
                 ORDER BY match_rank, length(name), updated_at DESC LIMIT @per)
                UNION ALL
                (SELECT 'Part', id, part_number AS title,
                        description AS subtitle, 'inventory_2' AS icon, '/parts' AS url,
                        CASE WHEN lower(part_number) = lower(@term) THEN 0
                             WHEN part_number ILIKE @prefix ESCAPE '\' THEN 1
                             WHEN part_number ILIKE @pattern ESCAPE '\' THEN 2
                             ELSE 3 END AS match_rank,
                        3 AS entity_order, updated_at
                 FROM parts
                 WHERE deleted_at IS NULL
                   AND (part_number ILIKE @pattern ESCAPE '\' OR description ILIKE @pattern ESCAPE '\')
                 ORDER BY match_rank, length(part_number), updated_at DESC LIMIT @per)
                UNION ALL
                (SELECT 'Lead', id, company_name AS title,
                        contact_name AS subtitle, 'trending_up' AS icon, '/leads' AS url,
                        CASE WHEN lower(company_name) = lower(@term) THEN 0
                             WHEN company_name ILIKE @prefix ESCAPE '\' THEN 1
                             WHEN company_name ILIKE @pattern ESCAPE '\' THEN 2
                             ELSE 3 END AS match_rank,
                        4 AS entity_order, updated_at
                 FROM leads
                 WHERE deleted_at IS NULL
                   AND (company_name ILIKE @pattern ESCAPE '\' OR contact_name ILIKE @pattern ESCAPE '\')
                 ORDER BY match_rank, length(company_name), updated_at DESC LIMIT @per)
                UNION ALL
                (SELECT 'Asset', id, name AS title,
                        serial_number AS subtitle, 'precision_manufacturing' AS icon, '/assets' AS url,
                        CASE WHEN lower(name) = lower(@term) THEN 0
                             WHEN name ILIKE @prefix ESCAPE '\' THEN 1
                             WHEN name ILIKE @pattern ESCAPE '\' THEN 2
                             ELSE 3 END AS match_rank,
                        5 AS entity_order, updated_at
                 FROM assets
                 WHERE deleted_at IS NULL
                   AND (name ILIKE @pattern ESCAPE '\' OR serial_number ILIKE @pattern ESCAPE '\')
                 ORDER BY match_rank, length(name), updated_at DESC LIMIT @per)
                UNION ALL
                (SELECT 'Expense', id, description AS title,
                        category AS subtitle, 'receipt_long' AS icon, '/expenses' AS url,
                        CASE WHEN lower(description) = lower(@term) THEN 0
                             WHEN description ILIKE @prefix ESCAPE '\' THEN 1
                             WHEN description ILIKE @pattern ESCAPE '\' THEN 2
                             ELSE 3 END AS match_rank,
                        6 AS entity_order, updated_at
                 FROM expenses
                 WHERE deleted_at IS NULL
                   AND (description ILIKE @pattern ESCAPE '\' OR category ILIKE @pattern ESCAPE '\')
                 ORDER BY match_rank, length(description), updated_at DESC LIMIT @per)
            ) ranked
            ORDER BY match_rank, length(title), entity_order, updated_at DESC
            LIMIT @limit
            """;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var literal = EscapeLike(term);
            command.Parameters.Add(new NpgsqlParameter("term", term));
            command.Parameters.Add(new NpgsqlParameter("prefix", $"{literal}%"));
            command.Parameters.Add(new NpgsqlParameter("pattern", $"%{literal}%"));
            command.Parameters.Add(new NpgsqlParameter("per", perEntity));
            command.Parameters.Add(new NpgsqlParameter("limit", limit));

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(new SearchResultModel(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5)));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return results;
    }

    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
