using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Search;

public record GlobalSearchQuery(string Term, int Limit = 20) : IRequest<List<SearchResultModel>>;

public class GlobalSearchHandler(ISearchRepository repo) : IRequestHandler<GlobalSearchQuery, List<SearchResultModel>>
{
    public Task<List<SearchResultModel>> Handle(GlobalSearchQuery request, CancellationToken cancellationToken)
    {
        return repo.SearchAsync(request.Term, request.Limit, cancellationToken);
    }
}
