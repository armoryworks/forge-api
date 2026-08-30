using MediatR;

using Forge.Api.Features.Search;

namespace Forge.Api.Features.Mobile;

public record LookupQuery(string Term) : IRequest<List<ScanResolveResponseModel>>;

/// <summary>
/// The Lookup screen's one search field: jobs, parts, customers, bins from
/// the global search, shaped like scan results so the same action sheet
/// applies to a typed result as to a scanned one.
/// </summary>
public class LookupHandler(IMediator mediator) : IRequestHandler<LookupQuery, List<ScanResolveResponseModel>>
{
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Job"] = "job",
        ["Part"] = "part",
        ["Customer"] = "customer",
        ["StorageLocation"] = "bin",
        ["Location"] = "bin",
        ["Lot"] = "lot",
    };

    public async Task<List<ScanResolveResponseModel>> Handle(LookupQuery request, CancellationToken ct)
    {
        var term = request.Term.Trim();
        if (term.Length < 2) return [];

        var results = await mediator.Send(new GlobalSearchQuery(term, 20), ct);
        return results
            .Where(r => Kinds.ContainsKey(r.EntityType))
            .Select(r => new ScanResolveResponseModel(Kinds[r.EntityType], r.EntityId, r.Title, r.Title, r.Subtitle))
            .ToList();
    }
}
