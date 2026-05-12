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
            (SELECT 'Job' AS entity_type, id AS entity_id, job_number AS title,
                    title AS subtitle, 'work' AS icon, '/kanban' AS url
             FROM jobs
             WHERE deleted_at IS NULL AND is_archived = false
               AND (job_number ILIKE @pattern OR title ILIKE @pattern)
             ORDER BY updated_at DESC LIMIT @per)
            UNION ALL
            (SELECT 'Customer', id, name, company_name, 'people', '/customers'
             FROM customers
             WHERE deleted_at IS NULL
               AND (name ILIKE @pattern OR company_name ILIKE @pattern)
             ORDER BY updated_at DESC LIMIT @per)
            UNION ALL
            (SELECT 'Part', id, part_number, description, 'inventory_2', '/parts'
             FROM parts
             WHERE deleted_at IS NULL
               AND (part_number ILIKE @pattern OR description ILIKE @pattern)
             ORDER BY updated_at DESC LIMIT @per)
            UNION ALL
            (SELECT 'Lead', id, company_name, contact_name, 'trending_up', '/leads'
             FROM leads
             WHERE deleted_at IS NULL
               AND (company_name ILIKE @pattern OR contact_name ILIKE @pattern)
             ORDER BY updated_at DESC LIMIT @per)
            UNION ALL
            (SELECT 'Asset', id, name, serial_number, 'precision_manufacturing', '/assets'
             FROM assets
             WHERE deleted_at IS NULL
               AND (name ILIKE @pattern OR serial_number ILIKE @pattern)
             ORDER BY updated_at DESC LIMIT @per)
            UNION ALL
            (SELECT 'Expense', id, description, category, 'receipt_long', '/expenses'
             FROM expenses
             WHERE deleted_at IS NULL
               AND (description ILIKE @pattern OR category ILIKE @pattern)
             ORDER BY updated_at DESC LIMIT @per)
            """;

        await using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new NpgsqlParameter("pattern", $"%{term}%"));
        command.Parameters.Add(new NpgsqlParameter("per", perEntity));

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

        return results.Take(limit).ToList();
    }
}
