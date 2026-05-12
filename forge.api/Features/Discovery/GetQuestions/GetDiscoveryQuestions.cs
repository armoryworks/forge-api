using MediatR;
using Forge.Api.Capabilities.Discovery;

namespace Forge.Api.Features.Discovery.GetQuestions;

/// <summary>
/// Phase 4 Phase-F — Returns the discovery question catalog. Self-serve mode
/// returns the 22 standard questions; consultant mode (per 4C decision #6)
/// adds the per-branch deepdive questions.
/// </summary>
public record GetDiscoveryQuestionsQuery(bool ConsultantMode) : IRequest<DiscoveryQuestionsResponseModel>;

public record DiscoveryQuestionsResponseModel(
    int TotalCount,
    int SelfServeCount,
    int ConsultantDeepdiveCount,
    IReadOnlyList<DiscoveryQuestionResponseModel> Questions);

public record DiscoveryQuestionResponseModel(
    string Id,
    string Stage,
    string Category,
    string Type,
    string Text,
    string WhyAsking,
    IReadOnlyList<DiscoveryChoiceResponseModel>? Choices,
    string? Branch);

public record DiscoveryChoiceResponseModel(string Value, string Label);

public class GetDiscoveryQuestionsHandler
    : IRequestHandler<GetDiscoveryQuestionsQuery, DiscoveryQuestionsResponseModel>
{
    public Task<DiscoveryQuestionsResponseModel> Handle(
        GetDiscoveryQuestionsQuery request,
        CancellationToken cancellationToken)
    {
        var filtered = DiscoveryQuestionCatalog.ForMode(request.ConsultantMode);
        var rendered = filtered
            .Select(q => new DiscoveryQuestionResponseModel(
                Id: q.Id,
                Stage: q.Stage.ToString(),
                Category: q.Category.ToString(),
                Type: q.Type.ToString(),
                Text: q.Text,
                WhyAsking: q.WhyAsking,
                Choices: q.Choices is null
                    ? null
                    : q.Choices.Select(c => new DiscoveryChoiceResponseModel(c.Value, c.Label)).ToList(),
                Branch: q.Branch))
            .ToList();

        return Task.FromResult(new DiscoveryQuestionsResponseModel(
            TotalCount: rendered.Count,
            SelfServeCount: DiscoveryQuestionCatalog.SelfServeCount,
            ConsultantDeepdiveCount: DiscoveryQuestionCatalog.ConsultantDeepdiveCount,
            Questions: rendered));
    }
}
