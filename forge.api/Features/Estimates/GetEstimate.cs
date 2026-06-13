using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Estimates;

public record GetEstimateQuery(int Id) : IRequest<EstimateDetailResponseModel>;

public class GetEstimateHandler(AppDbContext db) : IRequestHandler<GetEstimateQuery, EstimateDetailResponseModel>
{
    public async Task<EstimateDetailResponseModel> Handle(GetEstimateQuery request, CancellationToken ct)
    {
        var e = await db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.GeneratedQuote)
            .Include(x => x.Lines).ThenInclude(l => l.Part)
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.Type == QuoteType.Estimate && x.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Estimate {request.Id} not found.");

        string? assignedToName = null;
        if (e.AssignedToId.HasValue)
        {
            assignedToName = await db.Users
                .Where(u => u.Id == e.AssignedToId.Value)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefaultAsync(ct);
        }

        return new EstimateDetailResponseModel(
            e.Id,
            e.CustomerId,
            e.Customer.Name,
            e.Title ?? string.Empty,
            e.Description,
            e.EstimatedAmount ?? 0,
            e.Status.ToString(),
            e.ExpirationDate,
            e.Notes,
            e.AssignedToId,
            assignedToName,
            e.GeneratedQuote?.Id,
            e.ConvertedAt,
            e.CreatedAt,
            e.UpdatedAt,
            e.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new QuoteLineResponseModel(
                    l.Id,
                    l.PartId,
                    l.Part?.PartNumber,
                    l.Description,
                    l.Quantity,
                    l.UnitPrice,
                    l.Quantity * l.UnitPrice,
                    l.LineNumber,
                    l.Notes))
                .ToList());
    }
}
