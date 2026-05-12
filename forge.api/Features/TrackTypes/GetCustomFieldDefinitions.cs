using System.Text.Json;

using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TrackTypes;

public record GetCustomFieldDefinitionsQuery(int TrackTypeId) : IRequest<List<CustomFieldDefinitionModel>>;

public class GetCustomFieldDefinitionsHandler(
    ITrackTypeRepository repo) : IRequestHandler<GetCustomFieldDefinitionsQuery, List<CustomFieldDefinitionModel>>
{
    public async Task<List<CustomFieldDefinitionModel>> Handle(GetCustomFieldDefinitionsQuery request, CancellationToken ct)
    {
        var trackType = await repo.FindAsync(request.TrackTypeId, ct)
            ?? throw new KeyNotFoundException($"Track type with ID {request.TrackTypeId} not found.");

        if (string.IsNullOrWhiteSpace(trackType.CustomFieldDefinitions))
            return [];

        return JsonSerializer.Deserialize<List<CustomFieldDefinitionModel>>(
            trackType.CustomFieldDefinitions,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }
}
