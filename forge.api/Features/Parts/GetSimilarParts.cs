using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Returns existing parts whose name is trigram-similar to a proposed new name,
/// so the UI can ask "did you mean one of these?" before creating a duplicate.
/// </summary>
public record GetSimilarPartsQuery(string Name) : IRequest<List<PartSimilarityResultModel>>;

public class GetSimilarPartsHandler(IPartRepository repo)
    : IRequestHandler<GetSimilarPartsQuery, List<PartSimilarityResultModel>>
{
    // pg_trgm similarity floor (0.3 is Postgres' default similarity threshold)
    // and the max suggestions surfaced to the near-duplicate guard.
    private const double SimilarityThreshold = 0.3;
    private const int MaxResults = 5;

    public async Task<List<PartSimilarityResultModel>> Handle(GetSimilarPartsQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return new List<PartSimilarityResultModel>();

        return await repo.FindSimilarByNameAsync(request.Name, SimilarityThreshold, MaxResults, cancellationToken);
    }
}
