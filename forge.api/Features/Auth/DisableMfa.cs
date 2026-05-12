using System.Security.Claims;

using MediatR;

using Forge.Api.Services;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Auth;

public record DisableMfaCommand(ClaimsPrincipal User) : IRequest;

public class DisableMfaHandler(
    IMfaService mfaService,
    ISystemAuditWriter auditWriter) : IRequestHandler<DisableMfaCommand>
{
    public async Task Handle(DisableMfaCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(request.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException());

        await mfaService.DisableMfaAsync(userId, cancellationToken);

        await auditWriter.WriteAsync("MfaDisabled", userId,
            entityType: "ApplicationUser",
            entityId: userId,
            ct: cancellationToken);
    }
}
