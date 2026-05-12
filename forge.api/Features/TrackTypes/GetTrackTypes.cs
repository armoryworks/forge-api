using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TrackTypes;

public record GetTrackTypesQuery : IRequest<List<TrackTypeResponseModel>>;

public class GetTrackTypesHandler(ITrackTypeRepository repo) : IRequestHandler<GetTrackTypesQuery, List<TrackTypeResponseModel>>
{
    public Task<List<TrackTypeResponseModel>> Handle(GetTrackTypesQuery request, CancellationToken cancellationToken)
        => repo.GetAllAsync(cancellationToken);
}
