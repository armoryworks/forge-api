using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Bi;

public record GetBiApiKeysQuery : IRequest<List<BiApiKeyResponseModel>>;

public class GetBiApiKeysHandler(AppDbContext db)
    : IRequestHandler<GetBiApiKeysQuery, List<BiApiKeyResponseModel>>
{
    public async Task<List<BiApiKeyResponseModel>> Handle(
        GetBiApiKeysQuery request, CancellationToken cancellationToken)
    {
        var keys = await db.BiApiKeys
            .AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);

        return keys.Select(k => new BiApiKeyResponseModel
        {
            Id = k.Id,
            Name = k.Name,
            KeyPrefix = k.KeyPrefix,
            IsActive = k.IsActive,
            LastUsedAt = k.LastUsedAt,
            ExpiresAt = k.ExpiresAt,
            AllowedEntitySets = k.AllowedEntitySetsJson != null
                ? JsonSerializer.Deserialize<List<string>>(k.AllowedEntitySetsJson) : null,
            AllowedIps = k.AllowedIpsJson != null
                ? JsonSerializer.Deserialize<List<string>>(k.AllowedIpsJson) : null,
            CreatedAt = k.CreatedAt,
        }).ToList();
    }
}
