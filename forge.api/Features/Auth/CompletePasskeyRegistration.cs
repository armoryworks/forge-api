using System.Security.Claims;

using Fido2NetLib;
using MediatR;

using Forge.Api.Services;

namespace Forge.Api.Features.Auth;

public record CompletePasskeyRegistrationCommand(
    string Origin,
    AuthenticatorAttestationRawResponse Response,
    string? DeviceName) : IRequest<string>;

public class CompletePasskeyRegistrationHandler(
    IPasskeyService passkeys,
    IHttpContextAccessor httpContext)
    : IRequestHandler<CompletePasskeyRegistrationCommand, string>
{
    public Task<string> Handle(
        CompletePasskeyRegistrationCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(
            httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return passkeys.CompleteRegistrationAsync(
            userId, request.Origin, request.Response, request.DeviceName, cancellationToken);
    }
}
