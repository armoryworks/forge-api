using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Ai;

/// <summary>
/// ai-fleet-orchestration D-crux: surfaces <see cref="IAiHardwareAdvisor"/> to the onboarding UI.
/// Given the AI capabilities an install intends to enable and the chosen customization tier,
/// returns recommended host RAM/disk and a single-box-vs-distribute topology hint.
/// </summary>
public record GetAiHardwareAdviceQuery(
    IReadOnlyList<AiCapabilityFootprint> Capabilities,
    AiCustomizationTier Tier) : IRequest<AiHardwareRecommendation>;

public class GetAiHardwareAdviceHandler(IAiHardwareAdvisor advisor)
    : IRequestHandler<GetAiHardwareAdviceQuery, AiHardwareRecommendation>
{
    public Task<AiHardwareRecommendation> Handle(GetAiHardwareAdviceQuery request, CancellationToken ct)
        => Task.FromResult(advisor.Recommend(request.Capabilities ?? [], request.Tier));
}
