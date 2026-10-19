using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Search;
using Forge.Data.Context;

namespace Forge.Api.Features.Mobile;

public record LookupQuery(string Term) : IRequest<List<ScanResolveResponseModel>>;

/// <summary>
/// The Lookup screen's one search field: jobs, parts and customers from the
/// global search, plus active bins (by name or barcode) and lots (by lot
/// number), shaped like scan results so the same action sheet applies to a
/// typed result as to a scanned one.
/// </summary>
public class LookupHandler(IMediator mediator, AppDbContext db) : IRequestHandler<LookupQuery, List<ScanResolveResponseModel>>
{
    private const string LikeEscape = "\\";
    private const int MaxPerKind = 5;

    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Job"] = "job",
        ["Part"] = "part",
        ["Customer"] = "customer",
    };

    public async Task<List<ScanResolveResponseModel>> Handle(LookupQuery request, CancellationToken ct)
    {
        var term = request.Term.Trim();
        if (term.Length < 2) return [];

        var results = await mediator.Send(new GlobalSearchQuery(term, 20), ct);
        var found = results
            .Where(r => Kinds.ContainsKey(r.EntityType))
            .Select(r => new ScanResolveResponseModel(Kinds[r.EntityType], r.EntityId, r.Title, r.Title, r.Subtitle))
            .ToList();

        var pattern = "%" + EscapeLike(term) + "%";

        var bins = await db.StorageLocations.AsNoTracking()
            .Where(l => l.IsActive
                && (EF.Functions.ILike(l.Name, pattern, LikeEscape)
                    || (l.Barcode != null && EF.Functions.ILike(l.Barcode, pattern, LikeEscape))))
            .OrderBy(l => l.Name)
            .Select(l => new { l.Id, l.Name, l.Description })
            .Take(MaxPerKind)
            .ToListAsync(ct);
        found.AddRange(bins.Select(b => new ScanResolveResponseModel("bin", b.Id, b.Name, b.Name, b.Description)));

        var lots = await db.LotRecords.AsNoTracking()
            .Where(l => EF.Functions.ILike(l.LotNumber, pattern, LikeEscape))
            .OrderBy(l => l.LotNumber)
            .Select(l => new { l.Id, l.LotNumber })
            .Take(MaxPerKind)
            .ToListAsync(ct);
        found.AddRange(lots.Select(l => new ScanResolveResponseModel("lot", l.Id, l.LotNumber, l.LotNumber, null)));

        return found;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
