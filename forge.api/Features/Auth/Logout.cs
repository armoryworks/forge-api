using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using MediatR;

using Forge.Api.Services;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Auth;

public record LogoutCommand(string? Jti) : IRequest;

public class LogoutHandler(
    ISessionStore sessionStore,
    IHttpContextAccessor httpContext,
    ISystemAuditWriter auditWriter) : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Jti))
        {
            await sessionStore.RevokeSessionAsync(request.Jti, cancellationToken);
        }

        var userIdClaim = httpContext.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out var userId))
        {
            await auditWriter.WriteAsync("UserLoggedOut", userId,
                entityType: "ApplicationUser",
                entityId: userId,
                ct: cancellationToken);
        }
    }
}
