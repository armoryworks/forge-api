using System.Security.Claims;

using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Auth;

public record BeginMfaSetupCommand(ClaimsPrincipal User, string? DeviceName) : IRequest<MfaSetupResponseModel>;

public class BeginMfaSetupHandler(IMfaService mfaService) : IRequestHandler<BeginMfaSetupCommand, MfaSetupResponseModel>
{
    public async Task<MfaSetupResponseModel> Handle(BeginMfaSetupCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(request.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException());

        return await mfaService.BeginTotpSetupAsync(userId, request.DeviceName, cancellationToken);
    }
}
