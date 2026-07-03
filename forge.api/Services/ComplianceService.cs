using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// regulated-parts-safety C-1. Computes the additive union of the shop's active compliance
/// profiles: strictest required traceability (None &lt; Lot &lt; Serial), SDS-required if any
/// profile requires it, and the merged/deduped required-field rules. Enforced server-side.
/// </summary>
public sealed class ComplianceService(AppDbContext db) : IComplianceService
{
    public async Task<EffectiveComplianceRequirements> GetEffectiveRequirementsAsync(CancellationToken ct = default)
    {
        var active = await db.ComplianceProfiles
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Include(p => p.FieldRules)
            .ToListAsync(ct);

        var trace = active.Select(p => p.RequiredTraceabilityType)
            .DefaultIfEmpty(TraceabilityType.None)
            .Max(); // enum order None < Lot < Serial → strictest wins

        var sdsRequired = active.Any(p => p.SdsRequired);

        var fields = active
            .SelectMany(p => p.FieldRules)
            .Select(r => new RequiredComplianceField(r.FieldKey, r.ProcessStep))
            .Distinct()
            .ToList();

        return new EffectiveComplianceRequirements(trace, sdsRequired, fields);
    }

    public async Task<IReadOnlyList<string>> GetMissingRequiredFieldsAsync(
        string processStep, ISet<string> presentFields, CancellationToken ct = default)
    {
        var effective = await GetEffectiveRequirementsAsync(ct);
        return effective.RequiredFields
            .Where(f => string.Equals(f.ProcessStep, processStep, StringComparison.OrdinalIgnoreCase)
                && !presentFields.Contains(f.FieldKey))
            .Select(f => f.FieldKey)
            .Distinct()
            .ToList();
    }

    public async Task<IReadOnlyList<PartSafetyDataSheet>> GetAssemblySdsAsync(int assemblyPartId, CancellationToken ct = default)
    {
        // Walk the BOM breadth-first to collect every descendant part (cycle-safe).
        var allParts = new HashSet<int> { assemblyPartId };
        var frontier = new List<int> { assemblyPartId };
        while (frontier.Count > 0)
        {
            var children = await db.Set<BOMLine>().AsNoTracking()
                .Where(b => frontier.Contains(b.ParentPartId))
                .Select(b => b.ChildPartId)
                .Distinct()
                .ToListAsync(ct);
            frontier = children.Where(allParts.Add).ToList(); // Add returns true only for new ids
        }

        var sds = await db.PartSafetyDataSheets.AsNoTracking()
            .Where(s => allParts.Contains(s.PartId))
            .ToListAsync(ct);

        // Dedupe: the same SDS (document set) shared across materials collapses to one.
        return sds.GroupBy(s => s.DocumentSetId).Select(g => g.First()).ToList();
    }
}
