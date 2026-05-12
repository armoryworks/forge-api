using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Terminology;

public record GetTerminologyQuery : IRequest<List<TerminologyEntryResponseModel>>;

public class GetTerminologyHandler(ITerminologyRepository repo)
    : IRequestHandler<GetTerminologyQuery, List<TerminologyEntryResponseModel>>
{
    public async Task<List<TerminologyEntryResponseModel>> Handle(
        GetTerminologyQuery request, CancellationToken cancellationToken)
    {
        return await repo.GetAllAsync(cancellationToken);
    }
}
