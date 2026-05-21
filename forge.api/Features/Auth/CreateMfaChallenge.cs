using MediatR;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Auth;

public record CreateMfaChallengeCommand(string MfaPendingToken) : IRequest<MfaChallengeResponseModel>;

public class CreateMfaChallengeHandler(
    IMfaService mfaService,
    IMfaPreAuthTokenService preAuth) : IRequestHandler<CreateMfaChallengeCommand, MfaChallengeResponseModel>
{
    public async Task<MfaChallengeResponseModel> Handle(CreateMfaChallengeCommand request, CancellationToken cancellationToken)
    {
        // F-054: the MFA-pending token (issued by Login only after a successful
        // password check) is the proof of the first factor. The bound userId is
        // the ONLY source of identity here — never a caller-supplied id — so a
        // caller cannot obtain a challenge (and thus a full JWT) without first
        // passing the password step.
        var userId = preAuth.ValidateAndGetUserId(request.MfaPendingToken)
            ?? throw new UnauthorizedAccessException("Invalid or expired MFA session");

        return await mfaService.CreateChallengeAsync(userId, cancellationToken);
    }
}
