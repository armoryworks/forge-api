using FluentValidation;
using MediatR;

using Forge.Api.Capabilities;
using Forge.Api.Capabilities.Discovery;

namespace Forge.Api.Features.Discovery.Preview;

/// <summary>
/// Phase 4 Phase-F — Stateless recommendation preview. Given a discovery
/// answer set, returns the recommendation tuple (preset + confidence +
/// rationale + factors + alternatives + capability deltas) WITHOUT
/// persisting anything (per 4F implementation-plan decision #8 — preview
/// endpoint is the value of the "what-if" exploration).
/// </summary>
public record PreviewDiscoveryRecommendationCommand(
    IReadOnlyList<DiscoveryAnswerInput> Answers)
    : IRequest<DiscoveryRecommendationResponseModel>;

public record DiscoveryAnswerInput(string QuestionId, string Value);

public record DiscoveryRecommendationResponseModel(
    string PresetId,
    string PresetName,
    string PresetDescription,
    double Confidence,
    string ConfidenceLabel,
    string Rationale,
    IReadOnlyList<DiscoveryRecommendationFactorResponseModel> Factors,
    IReadOnlyList<DiscoveryAlternativeResponseModel> Alternatives,
    IReadOnlyList<CapabilityDeltaResponseModel> CapabilityDeltas);

public record DiscoveryRecommendationFactorResponseModel(string QuestionId, string Description);

public record DiscoveryAlternativeResponseModel(
    string PresetId,
    string PresetName,
    string DistinguishingRationale);

public record CapabilityDeltaResponseModel(
    string Code,
    string Name,
    bool CurrentlyEnabled,
    bool WillBeEnabled);

public class PreviewDiscoveryRecommendationValidator
    : AbstractValidator<PreviewDiscoveryRecommendationCommand>
{
    public PreviewDiscoveryRecommendationValidator()
    {
        RuleFor(x => x.Answers).NotNull();
        RuleForEach(x => x.Answers).ChildRules(item =>
        {
            item.RuleFor(i => i.QuestionId).NotEmpty().Matches("^Q-[A-Z][0-9A-Z]+$");
            item.RuleFor(i => i.Value).MaximumLength(8000);
        });
    }
}

public class PreviewDiscoveryRecommendationHandler(ICapabilitySnapshotProvider snapshots)
    : IRequestHandler<PreviewDiscoveryRecommendationCommand, DiscoveryRecommendationResponseModel>
{
    public Task<DiscoveryRecommendationResponseModel> Handle(
        PreviewDiscoveryRecommendationCommand request,
        CancellationToken cancellationToken)
    {
        var answerSet = new DiscoveryAnswerSet(
            request.Answers.Select(a => new DiscoveryAnswer(a.QuestionId, a.Value ?? string.Empty)));

        var recommendation = DiscoveryRecommendationEngine.Recommend(answerSet);

        var preset = PresetCatalog.FindById(recommendation.PresetId)
            ?? throw new InvalidOperationException(
                $"Recommendation produced unknown preset {recommendation.PresetId}");

        var deltas = DiscoveryRecommendationEngine.ComputeDeltas(
            recommendation.PresetId, snapshots.Current.EnabledByCode);

        return Task.FromResult(new DiscoveryRecommendationResponseModel(
            PresetId: recommendation.PresetId,
            PresetName: preset.Name,
            PresetDescription: preset.ShortDescription,
            Confidence: recommendation.Confidence,
            ConfidenceLabel: recommendation.ConfidenceLabel,
            Rationale: recommendation.Rationale,
            Factors: recommendation.Factors
                .Select(f => new DiscoveryRecommendationFactorResponseModel(f.QuestionId, f.Description))
                .ToList(),
            Alternatives: recommendation.Alternatives
                .Select(a => new DiscoveryAlternativeResponseModel(a.PresetId, a.PresetName, a.DistinguishingRationale))
                .ToList(),
            CapabilityDeltas: deltas
                .Select(d => new CapabilityDeltaResponseModel(d.Code, d.Name, d.CurrentlyEnabled, d.WillBeEnabled))
                .ToList()));
    }
}
