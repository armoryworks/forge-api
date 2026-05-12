using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.ReferenceData;

public record GetReferenceDataGroupsQuery : IRequest<List<ReferenceDataGroupResponseModel>>;

public class GetReferenceDataGroupsHandler(IReferenceDataRepository repo) : IRequestHandler<GetReferenceDataGroupsQuery, List<ReferenceDataGroupResponseModel>>
{
    public Task<List<ReferenceDataGroupResponseModel>> Handle(GetReferenceDataGroupsQuery request, CancellationToken cancellationToken)
        => repo.GetAllGroupsAsync(cancellationToken);
}
