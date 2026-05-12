using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Capabilities;
using Forge.Data.Context;

namespace Forge.Api.Features.Capabilities.Descriptor;

/// <summary>
/// Phase 4 Phase-A — Read-only capability descriptor query. Returns the full
/// capability set with current state, defaults, and role hints. Authenticated
/// users only (per 4D decision #2 and 4D §5.1). The UI consumes this on
/// post-login bootstrap and on capability-changed SignalR events (Phase C).
///
/// Phase 4 Phase-C — Each row carries a <c>Version</c> + <c>ETag</c> so the
/// admin UI can submit If-Match on subsequent toggle / config writes (4D §5.4).
/// </summary>
public record GetCapabilityDescriptorQuery() : IRequest<CapabilityDescriptorResponseModel>;

public record CapabilityDescriptorResponseModel(
    DateTimeOffset GeneratedAt,
    int TotalCount,
    int EnabledCount,
    IReadOnlyList<CapabilityDescriptorEntry> Capabilities);

public record CapabilityDescriptorEntry(
    string Id,
    string Code,
    string Area,
    string Name,
    string Description,
    bool Enabled,
    bool IsDefaultOn,
    string? RequiresRoles,
    uint Version,
    string ETag,
    uint? ConfigVersion,
    string? ConfigETag,
    int? ConfigId,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Mutexes);

public class GetCapabilityDescriptorHandler(AppDbContext db, ICapabilitySnapshotProvider snapshots)
    : IRequestHandler<GetCapabilityDescriptorQuery, CapabilityDescriptorResponseModel>
{
    public async Task<CapabilityDescriptorResponseModel> Handle(
        GetCapabilityDescriptorQuery request,
        CancellationToken cancellationToken)
    {
        // Pre-compute dependency / mutex maps once per request.
        var depsByFrom = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in CapabilityCatalogRelations.Dependencies)
        {
            if (!depsByFrom.TryGetValue(edge.From, out var list))
            {
                list = new List<string>();
                depsByFrom[edge.From] = list;
            }
            list.Add(edge.To);
        }
        var mutexByCode = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in CapabilityCatalogRelations.Mutexes)
        {
            if (!mutexByCode.TryGetValue(edge.From, out var fromList))
            {
                fromList = new List<string>();
                mutexByCode[edge.From] = fromList;
            }
            fromList.Add(edge.To);
            if (!mutexByCode.TryGetValue(edge.To, out var toList))
            {
                toList = new List<string>();
                mutexByCode[edge.To] = toList;
            }
            toList.Add(edge.From);
        }

        // Pre-load configs (1:0..1) keyed by capability id.
        var configs = await db.CapabilityConfigs
            .AsNoTracking()
            .ToDictionaryAsync(c => c.CapabilityId, cancellationToken);

        var rows = await db.Capabilities
            .AsNoTracking()
            .OrderBy(c => c.Area).ThenBy(c => c.Code)
            .Select(c => new
            {
                c.Id,
                c.Code,
                c.Area,
                c.Name,
                c.Description,
                c.Enabled,
                c.IsDefaultOn,
                c.RequiresRoles,
                c.Version,
            })
            .ToListAsync(cancellationToken);

        var entries = rows
            .Select(r =>
            {
                configs.TryGetValue(r.Id, out var cfg);
                IReadOnlyList<string> deps = depsByFrom.TryGetValue(r.Code, out var d) ? d : Array.Empty<string>();
                IReadOnlyList<string> muts = mutexByCode.TryGetValue(r.Code, out var m) ? m : Array.Empty<string>();
                return new CapabilityDescriptorEntry(
                    Id: r.Code,
                    Code: r.Code,
                    Area: r.Area,
                    Name: r.Name,
                    Description: r.Description,
                    Enabled: r.Enabled,
                    IsDefaultOn: r.IsDefaultOn,
                    RequiresRoles: r.RequiresRoles,
                    Version: r.Version,
                    ETag: $"W/\"{r.Version}\"",
                    ConfigVersion: cfg?.Version,
                    ConfigETag: cfg is null ? null : $"W/\"{cfg.Version}\"",
                    ConfigId: cfg?.Id,
                    Dependencies: deps,
                    Mutexes: muts);
            })
            .ToList();

        var enabledCount = entries.Count(r => r.Enabled);

        return new CapabilityDescriptorResponseModel(
            GeneratedAt: snapshots.Current.GeneratedAt,
            TotalCount: entries.Count,
            EnabledCount: enabledCount,
            Capabilities: entries);
    }
}
