using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Ai;

public record CheckAiAvailabilityQuery : IRequest<AiAvailabilityResponse>;
public record AiAvailabilityResponse(bool Available);

public class CheckAiAvailabilityHandler(IAiService aiService) : IRequestHandler<CheckAiAvailabilityQuery, AiAvailabilityResponse>
{
    public async Task<AiAvailabilityResponse> Handle(CheckAiAvailabilityQuery request, CancellationToken ct)
    {
        var available = await aiService.IsAvailableAsync(ct);
        return new AiAvailabilityResponse(available);
    }
}
