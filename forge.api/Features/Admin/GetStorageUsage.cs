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
        return await db.FileAttachments
            .Where(f => f.DeletedAt == null)
            .GroupBy(f => f.EntityType)
            .Select(g => new StorageUsageResponseModel(
                g.Key,
                g.Count(),
                g.Sum(f => f.Size)))
            .OrderByDescending(s => s.TotalSizeBytes)
            .ToListAsync(ct);
    }
}
