using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Data.Context;

namespace Forge.Api.Features.Capabilities.Relations;

/// <summary>
/// Phase 4 Phase-E — Returns the dependency / dependent / mutex set for a
/// single capability, augmented with each peer's current name, area, and
/// enabled state. Drives the per-capability detail page's "Dependencies" +
/// "Required by" + "Mutually exclusive with" sections (4E §Screen 5).
///
/// Without this endpoint the UI would have to walk the descriptor's full
/// capability list to compute the inverse-dependency graph (dependents) on
/// every detail-page load. Centralising the lookup server-side keeps the UI
/// simple and lets future caching land in one place.
/// </summary>
public record GetCapabilityRelationsQuery(string Code)
    : IRequest<CapabilityRelationsResponseModel>;

public record CapabilityRelationsResponseModel(
    string Code,
    IReadOnlyList<CapabilityRelationEntry> Dependencies,
    IReadOnlyList<CapabilityRelationEntry> Dependents,
    IReadOnlyList<CapabilityRelationEntry> Mutexes);

public record CapabilityRelationEntry(
    string Code,
    string Name,
    string Area,
    bool Enabled);

public class GetCapabilityRelationsHandler(AppDbContext db)
    : IRequestHandler<GetCapabilityRelationsQuery, CapabilityRelationsResponseModel>
{
    public async Task<CapabilityRelationsResponseModel> Handle(
        GetCapabilityRelationsQuery request,
        CancellationToken ct)
    {
        // Verify the capability exists before computing relations.
        var exists = await db.Capabilities
            .AsNoTracking()
            .AnyAsync(c => c.Code == request.Code, ct);
        if (!exists)
            throw new KeyNotFoundException($"Capability '{request.Code}' not found.");

        // Walk the static catalog edges. A row's dependencies are the To-side
        // of every Dependency edge whose From is this code; its dependents
        // are the From-side of every edge whose To is this code (inverse
        // graph). Mutex peers are bidirectional.
        var depCodes = CapabilityCatalogRelations.Dependencies
            .Where(e => e.From == request.Code)
            .Select(e => e.To)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var dependentCodes = CapabilityCatalogRelations.Dependencies
            .Where(e => e.To == request.Code)
            .Select(e => e.From)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var mutexCodes = CapabilityCatalogRelations.Mutexes
            .Where(e => e.From == request.Code || e.To == request.Code)
            .Select(e => e.From == request.Code ? e.To : e.From)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var allPeerCodes = depCodes.Concat(dependentCodes).Concat(mutexCodes)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Single round-trip to load every peer's current state.
        var peers = await db.Capabilities
            .AsNoTracking()
            .Where(c => allPeerCodes.Contains(c.Code))
            .Select(c => new { c.Code, c.Name, c.Area, c.Enabled })
            .ToListAsync(ct);

        var peerByCode = peers.ToDictionary(p => p.Code, StringComparer.Ordinal);

        IReadOnlyList<CapabilityRelationEntry> Build(IEnumerable<string> codes)
            => codes
                .Select(code =>
                {
                    if (peerByCode.TryGetValue(code, out var p))
                    {
                        return new CapabilityRelationEntry(p.Code, p.Name, p.Area, p.Enabled);
                    }
                    // Catalog-drift: edge points at a code not in the seed.
                    // Surface a stub so the UI can still render the edge.
                    return new CapabilityRelationEntry(code, code, string.Empty, false);
                })
                .OrderBy(e => e.Area, StringComparer.Ordinal)
                .ThenBy(e => e.Code, StringComparer.Ordinal)
                .ToList();

        return new CapabilityRelationsResponseModel(
            Code: request.Code,
            Dependencies: Build(depCodes),
            Dependents: Build(dependentCodes),
            Mutexes: Build(mutexCodes));
    }
}
