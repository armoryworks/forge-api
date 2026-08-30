using System.Security.Claims;

using Fido2NetLib;
using MediatR;

using Forge.Api.Services;

namespace Forge.Api.Features.Auth;

public record BeginPasskeyRegistrationCommand(string Origin) : IRequest<CredentialCreateOptions>;

public class BeginPasskeyRegistrationHandler(
    IPasskeyService passkeys,
    IHttpContextAccessor httpContext)
    : IRequestHandler<BeginPasskeyRegistrationCommand, CredentialCreateOptions>
{
    public Task<CredentialCreateOptions> Handle(
        BeginPasskeyRegistrationCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(
            httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return passkeys.BeginRegistrationAsync(userId, request.Origin, cancellationToken);
    }
}
