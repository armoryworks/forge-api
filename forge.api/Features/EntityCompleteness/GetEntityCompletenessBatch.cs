using FluentValidation;

using MediatR;

using Forge.Core.Models;

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

public class GetEntityCompletenessBatchHandler(EntityCompletenessEvaluator evaluator)
    : IRequestHandler<GetEntityCompletenessBatchQuery, IReadOnlyList<EntityCompletenessResponseModel>>
{
    public Task<IReadOnlyList<EntityCompletenessResponseModel>> Handle(
        GetEntityCompletenessBatchQuery request,
        CancellationToken cancellationToken) =>
        evaluator.EvaluateAsync(request.EntityType, request.ParseIds(), cancellationToken);
}
