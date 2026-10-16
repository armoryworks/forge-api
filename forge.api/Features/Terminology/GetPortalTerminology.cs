using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Terminology;

public record GetPortalTerminologyQuery : IRequest<List<TerminologyEntryResponseModel>>;

public class GetPortalTerminologyHandler(ITerminologyRepository repo)
    : IRequestHandler<GetPortalTerminologyQuery, List<TerminologyEntryResponseModel>>
{
    public async Task<List<TerminologyEntryResponseModel>> Handle(
        GetPortalTerminologyQuery request, CancellationToken cancellationToken)
    {
        var entries = await repo.GetAllAsync(cancellationToken);
        return entries
            .Where(e => TerminologyTermCatalog.IsTermKey(e.Key))
            .Select(e => new TerminologyEntryResponseModel(e.Key, e.Label))
            .ToList();
    }
}
