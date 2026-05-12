using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ISearchRepository
{
    Task<List<SearchResultModel>> SearchAsync(string term, int limit, CancellationToken ct);
}
