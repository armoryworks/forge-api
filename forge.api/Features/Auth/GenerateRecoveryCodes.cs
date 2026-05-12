using System.Security.Claims;

using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Auth;

public record GenerateRecoveryCodesCommand(ClaimsPrincipal User) : IRequest<MfaRecoveryCodesResponseModel>;

public class GenerateRecoveryCodesHandler(IMfaService mfaService) : IRequestHandler<GenerateRecoveryCodesCommand, MfaRecoveryCodesResponseModel>
{
    public async Task<MfaRecoveryCodesResponseModel> Handle(GenerateRecoveryCodesCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(request.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException());

        return await mfaService.GenerateRecoveryCodesAsync(userId, cancellationToken);
    }
}
