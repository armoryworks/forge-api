using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Auth;

public record CreateMfaChallengeCommand(int UserId) : IRequest<MfaChallengeResponseModel>;

public class CreateMfaChallengeHandler(IMfaService mfaService) : IRequestHandler<CreateMfaChallengeCommand, MfaChallengeResponseModel>
{
    public async Task<MfaChallengeResponseModel> Handle(CreateMfaChallengeCommand request, CancellationToken cancellationToken)
    {
        return await mfaService.CreateChallengeAsync(request.UserId, cancellationToken);
    }
}
