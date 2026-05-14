using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.AutoPo;

public record GetAutoPoSuggestionsQuery(AutoPoSuggestionStatus? Status) : IRequest<List<AutoPoSuggestionResponseModel>>;

public class GetAutoPoSuggestionsHandler(AppDbContext db) : IRequestHandler<GetAutoPoSuggestionsQuery, List<AutoPoSuggestionResponseModel>>
{
    public async Task<List<AutoPoSuggestionResponseModel>> Handle(GetAutoPoSuggestionsQuery request, CancellationToken ct)
    {
        var query = db.AutoPoSuggestions
            .AsNoTracking()
            .Include(s => s.Part)
            .Include(s => s.Vendor)
            .AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(s => s.Status == request.Status.Value);

        var suggestions = await query
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        // Pre-fetch SO numbers for all suggestions that reference them
        var allSoIds = new HashSet<int>();
        foreach (var s in suggestions)
        {
            if (string.IsNullOrWhiteSpace(s.SourceSalesOrderIds)) continue;
            var ids = JsonSerializer.Deserialize<List<int>>(s.SourceSalesOrderIds);
            if (ids is not null)
                allSoIds.UnionWith(ids);
        }

        var soNumberMap = allSoIds.Count > 0
            ? await db.SalesOrders
                .AsNoTracking()
                .Where(so => allSoIds.Contains(so.Id))
                .ToDictionaryAsync(so => so.Id, so => so.OrderNumber, ct)
            : new Dictionary<int, string>();

        return suggestions.Select(s =>
        {
            List<string>? soNumbers = null;
            if (!string.IsNullOrWhiteSpace(s.SourceSalesOrderIds))
            {
                var ids = JsonSerializer.Deserialize<List<int>>(s.SourceSalesOrderIds);
                soNumbers = ids?.Select(id => soNumberMap.TryGetValue(id, out var num) ? num : $"SO-{id}").ToList();
            }

            return new AutoPoSuggestionResponseModel(
                s.Id, s.PartId, s.Part.PartNumber, s.Part.Description,
                s.VendorId, s.Vendor.CompanyName, s.SuggestedQty,
                s.NeededByDate, s.Status.ToString(), soNumbers, s.CreatedAt);
        }).ToList();
    }
}
