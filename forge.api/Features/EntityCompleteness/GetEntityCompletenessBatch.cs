using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Workflows;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.EntityCompleteness;

/// <summary>
/// Completeness breakdown for a page of entities of one type in a single
/// round trip, so a list rendering a chip or badge per row does not fire one
/// request per row. <see cref="Ids"/> is the raw comma-separated query value
/// (at most <see cref="GetEntityCompletenessBatchValidator.MaxIds"/> ids).
/// Returns one entry per distinct id that resolves to an entity, in request
/// order; ids that do not resolve are skipped rather than failing the batch.
/// </summary>
public record GetEntityCompletenessBatchQuery(string EntityType, string? Ids)
    : IRequest<IReadOnlyList<EntityCompletenessResponseModel>>
{
    public IReadOnlyList<int> ParseIds() =>
        (Ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => int.TryParse(token, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
}

public class GetEntityCompletenessBatchValidator : AbstractValidator<GetEntityCompletenessBatchQuery>
{
    public const int MaxIds = 200;

    public GetEntityCompletenessBatchValidator()
    {
        RuleFor(x => x.EntityType).NotEmpty();
        RuleFor(x => x.Ids)
            .NotEmpty()
            .Must(BeIdList).WithMessage("ids must be a comma-separated list of positive integers.")
            .Must(ids => CountTokens(ids) <= MaxIds).WithMessage($"At most {MaxIds} ids per request.");
    }

    private static bool BeIdList(string? ids) =>
        !string.IsNullOrWhiteSpace(ids)
        && ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(token => int.TryParse(token, out var id) && id > 0);

    private static int CountTokens(string? ids) =>
        (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
}

public class GetEntityCompletenessBatchHandler(
    AppDbContext db,
    ICapabilitySnapshotProvider snapshots,
    PredicateEvaluator evaluator)
    : IRequestHandler<GetEntityCompletenessBatchQuery, IReadOnlyList<EntityCompletenessResponseModel>>
{
    public async Task<IReadOnlyList<EntityCompletenessResponseModel>> Handle(
        GetEntityCompletenessBatchQuery request,
        CancellationToken cancellationToken)
    {
        var ids = request.ParseIds();
        if (ids.Count == 0)
            return [];

        var entities = await LoadEntitiesAsync(request.EntityType, ids, cancellationToken);
        if (entities.Count == 0)
            return [];

        var requirements = await db.EntityCapabilityRequirements.AsNoTracking()
            .Where(r => r.EntityType == request.EntityType)
            .OrderBy(r => r.CapabilityCode)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.RequirementId)
            .ToListAsync(cancellationToken);

        var snapshot = snapshots.Current;
        var enabledRequirements = requirements
            .Where(r => snapshot.IsEnabled(r.CapabilityCode))
            .GroupBy(r => r.CapabilityCode)
            .ToList();

        var byCode = CapabilityCatalog.All.ToDictionary(c => c.Code, c => c.Name);
        var results = new List<EntityCompletenessResponseModel>(entities.Count);

        foreach (var id in ids)
        {
            if (!entities.TryGetValue(id, out var entity))
                continue;

            var capabilities = enabledRequirements
                .Select(group =>
                {
                    var missing = group
                        .Where(requirement => !evaluator.Evaluate(requirement.Predicate, entity))
                        .Select(requirement => new EntityCompletenessMissingField(
                            requirement.RequirementId,
                            requirement.DisplayNameKey,
                            requirement.MissingMessageKey))
                        .ToList();
                    return new EntityCompletenessCapability(
                        group.Key,
                        byCode.GetValueOrDefault(group.Key, group.Key),
                        missing.Count == 0,
                        missing);
                })
                .ToList();

            results.Add(new EntityCompletenessResponseModel(request.EntityType, id, capabilities));
        }

        return results;
    }

    private async Task<Dictionary<int, object>> LoadEntitiesAsync(
        string entityType, IReadOnlyList<int> ids, CancellationToken ct)
    {
        return entityType switch
        {
            "Vendor" => await db.Vendors.AsNoTracking()
                .Where(v => ids.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => (object)v, ct),
            "Part" => await db.Parts.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => (object)p, ct),
            "Customer" => await db.Customers.AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => (object)c, ct),
            _ => [],
        };
    }
}
