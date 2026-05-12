using System.Security.Claims;

using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Auth;

public record RemoveMfaDeviceCommand(ClaimsPrincipal User, int DeviceId) : IRequest;

public class RemoveMfaDeviceHandler(IMfaService mfaService) : IRequestHandler<RemoveMfaDeviceCommand>
{
    public async Task Handle(RemoveMfaDeviceCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(request.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException());

        await mfaService.RemoveDeviceAsync(userId, request.DeviceId, cancellationToken);
    }
}
