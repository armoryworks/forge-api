using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities.Discovery.Bundles;
using Forge.Api.Features.Presets.Apply.Layers;
using Forge.Data.Context;

namespace Forge.Api.Features.CloudStorage;

public record GetCloudStorageFolderMapQuery : IRequest<List<FolderMapSuggestion>>;

public class GetCloudStorageFolderMapHandler(AppDbContext db) : IRequestHandler<GetCloudStorageFolderMapQuery, List<FolderMapSuggestion>>
{
    public async Task<List<FolderMapSuggestion>> Handle(GetCloudStorageFolderMapQuery request, CancellationToken ct)
    {
        var raw = await db.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key == FolderMapBundleApplier.FolderMapSettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(raw)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<FolderMapSuggestion>>(raw) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
