using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TrackTypes;

public record GetTrackTypeByIdQuery(int Id) : IRequest<TrackTypeResponseModel>;

public class GetTrackTypeByIdHandler(ITrackTypeRepository repo) : IRequestHandler<GetTrackTypeByIdQuery, TrackTypeResponseModel>
{
    public async Task<TrackTypeResponseModel> Handle(GetTrackTypeByIdQuery request, CancellationToken cancellationToken)
    {
        var trackType = await repo.GetByIdAsync(request.Id, cancellationToken);
        return trackType ?? throw new KeyNotFoundException($"Track type with ID {request.Id} not found.");
    }
}
