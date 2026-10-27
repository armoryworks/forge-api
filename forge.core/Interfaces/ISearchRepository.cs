using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ISearchRepository
{
    Task<List<SearchResultModel>> SearchAsync(string term, int limit, CancellationToken ct);

    /// <summary>
    /// As <see cref="SearchAsync(string, int, CancellationToken)"/>; when <paramref name="includeContacts"/> is true the
    /// results also match customer contacts by name, email and phone.
    /// </summary>
    Task<List<SearchResultModel>> SearchAsync(string term, int limit, bool includeContacts, CancellationToken ct);
}
