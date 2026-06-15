using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetStorageUsageQuery() : IRequest<List<StorageUsageResponseModel>>;

public class GetStorageUsageHandler(AppDbContext db) : IRequestHandler<GetStorageUsageQuery, List<StorageUsageResponseModel>>
{
    public async Task<List<StorageUsageResponseModel>> Handle(GetStorageUsageQuery request, CancellationToken ct)
    {
        // EF Core can't translate GroupBy projected into a constructor and then
        // ordered by the projected property (it threw a query-translation 500).
        // Aggregate into an anonymous type in SQL — which IS translatable — then
        // order + project to the response model client-side on the small grouped
        // result set.
        var grouped = await db.FileAttachments
            .Where(f => f.DeletedAt == null)
            .GroupBy(f => f.EntityType)
            .Select(g => new
            {
                EntityType = g.Key,
                FileCount = g.Count(),
                TotalSizeBytes = g.Sum(f => f.Size),
            })
            .ToListAsync(ct);

        return grouped
            .OrderByDescending(g => g.TotalSizeBytes)
            .Select(g => new StorageUsageResponseModel(g.EntityType, g.FileCount, g.TotalSizeBytes))
            .ToList();
    }
}
