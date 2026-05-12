using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.ReferenceData;

public record GetReferenceDataByGroupQuery(string GroupCode) : IRequest<List<ReferenceDataResponseModel>>;

public class GetReferenceDataByGroupHandler(IReferenceDataRepository repo) : IRequestHandler<GetReferenceDataByGroupQuery, List<ReferenceDataResponseModel>>
{
    public Task<List<ReferenceDataResponseModel>> Handle(GetReferenceDataByGroupQuery request, CancellationToken cancellationToken)
        => repo.GetByGroupAsync(request.GroupCode, cancellationToken);
}
