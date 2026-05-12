using FluentValidation;
using MediatR;

using Forge.Api.Capabilities;
using Forge.Api.Capabilities.Discovery;
using Forge.Api.Features.Presets.List;
using Forge.Api.Features.Presets.Models;

namespace Forge.Api.Features.Presets.Compare;

/// <summary>
/// Phase 4 Phase-G — Side-by-side preset comparison. Returns a matrix with
/// one row per capability and one column per selected preset; each cell is
/// "in/out of this preset". Supports 2-4 presets per the prompt.
///
/// Layout decision (4G-decisions): rows = capabilities, columns = presets.
/// Capabilities outnumber presets ~30:1, so a tall matrix beats a wide one
/// for screen real estate and aligns with the existing data-table pattern.
/// </summary>
public record ComparePresetsQuery(IReadOnlyList<string> PresetIds)
    : IRequest<PresetCompareResponseModel>;

public class ComparePresetsValidator : AbstractValidator<ComparePresetsQuery>
{
    public ComparePresetsValidator()
    {
        RuleFor(x => x.PresetIds)
            .NotNull()
            .Must(ids => ids.Count >= 2 && ids.Count <= 4)
            .WithMessage("Compare requires between 2 and 4 preset IDs.")
            .Must(ids => ids.Distinct(StringComparer.Ordinal).Count() == ids.Count)
            .WithMessage("Duplicate preset IDs are not allowed.");
        RuleForEach(x => x.PresetIds)
            .NotEmpty()
            .Matches("^PRESET-[A-Z0-9-]+$");
    }
}

public class ComparePresetsHandler(ICapabilitySnapshotProvider snapshots)
    : IRequestHandler<ComparePresetsQuery, PresetCompareResponseModel>
{
    public Task<PresetCompareResponseModel> Handle(
        ComparePresetsQuery request,
        CancellationToken cancellationToken)
    {
        var snapshot = snapshots.Current;

        // Resolve presets in the requested order. Unknown ids surface as
        // a not-found 404 via the controller (we throw here for the
        // standard middleware to catch).
        var presets = request.PresetIds
            .Select(id => PresetCatalog.FindById(id)
                ?? throw new KeyNotFoundException($"Unknown preset id: {id}"))
            .ToList();

        // For each preset, resolve its effective capability set (Custom
        // inherits catalog defaults).
        var defaultsSet = new HashSet<string>(
            CapabilityCatalog.All.Where(c => c.IsDefaultOn).Select(c => c.Code),
            StringComparer.Ordinal);
        var presetSets = presets
            .Select(p => p.IsCustom
                ? defaultsSet
                : new HashSet<string>(p.EnabledCapabilities, StringComparer.Ordinal))
            .ToList();

        var summaries = presets
            .Select(p => new PresetSummaryResponseModel(
                Id: p.Id,
                Name: p.Name,
                ShortDescription: p.ShortDescription,
                TargetProfile: p.TargetProfile,
                CapabilityCount: p.IsCustom
                    ? defaultsSet.Count
                    : p.EnabledCapabilities.Count,
                IsCustom: p.IsCustom,
                IsActive: !p.IsCustom && PresetStateComparer.MatchesCurrent(p, snapshot.EnabledByCode),
                RecommendedFor: PresetMetadata.GetRecommendedForTags(p.Id)))
            .ToList();

        // One row per capability — column cells indicate membership in each
        // preset. Rows where the cells disagree (i.e. some IN, some OUT)
        // are flagged so the UI can highlight them.
        var rows = CapabilityCatalog.All
            .OrderBy(c => c.Area, StringComparer.Ordinal)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .Select(c =>
            {
                var cells = presets
                    .Select((p, idx) => new PresetCompareCellResponseModel(
                        PresetId: p.Id,
                        InPreset: presetSets[idx].Contains(c.Code)))
                    .ToList();
                var anyIn = cells.Any(x => x.InPreset);
                var anyOut = cells.Any(x => !x.InPreset);
                var disagreement = anyIn && anyOut;
                return new PresetCompareCapabilityRowResponseModel(
                    Code: c.Code,
                    Name: c.Name,
                    Area: c.Area,
                    DefaultOn: c.IsDefaultOn,
                    Cells: cells,
                    Disagreement: disagreement);
            })
            .ToList();

        return Task.FromResult(new PresetCompareResponseModel(
            Presets: summaries,
            Rows: rows));
    }
}
