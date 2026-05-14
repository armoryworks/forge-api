using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Replenishment;

public record GetReorderSuggestionsQuery(ReorderSuggestionStatus? Status) : IRequest<List<ReorderSuggestionResponseModel>>;

public class GetReorderSuggestionsHandler(AppDbContext db)
    : IRequestHandler<GetReorderSuggestionsQuery, List<ReorderSuggestionResponseModel>>
{
    public async Task<List<ReorderSuggestionResponseModel>> Handle(
        GetReorderSuggestionsQuery request, CancellationToken cancellationToken)
    {
        var query = db.ReorderSuggestions
            .Include(s => s.Part)
            .Include(s => s.Vendor)
            .AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(s => s.Status == request.Status.Value);

        var suggestions = await query
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        // Collect user IDs for name resolution
        var userIds = suggestions
            .SelectMany(s => new[] { s.ApprovedByUserId, s.DismissedByUserId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var users = userIds.Count > 0
            ? await db.Users
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName })
                .ToListAsync(cancellationToken)
            : [];

        var userMap = users.ToDictionary(u => u.Id, u => $"{u.LastName}, {u.FirstName}");

        return suggestions.Select(s => new ReorderSuggestionResponseModel(
            s.Id,
            s.PartId,
            s.Part.PartNumber,
            s.Part.Description ?? s.Part.Name,
            s.VendorId,
            s.Vendor?.CompanyName,
            s.CurrentStock,
            s.AvailableStock,
            s.BurnRateDailyAvg,
            s.BurnRateWindowDays,
            s.DaysOfStockRemaining,
            s.ProjectedStockoutDate,
            s.IncomingPoQuantity,
            s.EarliestPoArrival,
            s.SuggestedQuantity,
            s.Status,
            s.ApprovedByUserId.HasValue ? userMap.GetValueOrDefault(s.ApprovedByUserId.Value) : null,
            s.ApprovedAt,
            s.ResultingPurchaseOrderId,
            s.DismissReason,
            s.DismissedByUserId.HasValue ? userMap.GetValueOrDefault(s.DismissedByUserId.Value) : null,
            s.DismissedAt,
            s.Notes,
            s.CreatedAt
        )).ToList();
    }
}
