using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CloudStorage;

public record GetCloudStorageProvidersQuery : IRequest<List<CloudStorageProviderResponseModel>>;

public class GetCloudStorageProvidersHandler(AppDbContext db)
    : IRequestHandler<GetCloudStorageProvidersQuery, List<CloudStorageProviderResponseModel>>
{
    public async Task<List<CloudStorageProviderResponseModel>> Handle(GetCloudStorageProvidersQuery request, CancellationToken ct)
    {
        var providers = await db.CloudStorageProviders
            .AsNoTracking()
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.ProviderCode)
            .ToListAsync(ct);

        var ids = providers.Select(p => p.Id).ToList();
        var links = await db.EntityCloudLinks
            .AsNoTracking()
            .Where(l => ids.Contains(l.ProviderId))
            .GroupBy(l => l.ProviderId)
            .Select(g => new { ProviderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProviderId, x => x.Count, ct);

        return providers
            .Select(p => CloudStorageProviderCodes.ToModel(p, links.GetValueOrDefault(p.Id)))
            .ToList();
    }
}
