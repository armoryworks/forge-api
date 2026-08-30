using Fido2NetLib;
using FluentValidation;
using MediatR;

using Forge.Api.Services;

namespace Forge.Api.Features.Auth;

public record BeginPasskeyMfaCommand(string MfaPendingToken, string Origin)
    : IRequest<AssertionOptions?>;

public class BeginPasskeyMfaValidator : AbstractValidator<BeginPasskeyMfaCommand>
{
    public BeginPasskeyMfaValidator()
    {
        RuleFor(x => x.MfaPendingToken).NotEmpty();
    }
}

/// <summary>
/// Passkey as the second factor: the MFA-pending token (issued by Login after
/// the password check) proves the first factor and binds the user; returns
/// assertion options for that user's passkeys, or null when they have none —
/// the caller falls back to TOTP.
/// </summary>
public class BeginPasskeyMfaHandler(
    IPasskeyService passkeys,
    IMfaPreAuthTokenService preAuth)
    : IRequestHandler<BeginPasskeyMfaCommand, AssertionOptions?>
{
    public Task<AssertionOptions?> Handle(
        BeginPasskeyMfaCommand request, CancellationToken cancellationToken)
    {
        var userId = preAuth.ValidateAndGetUserId(request.MfaPendingToken)
            ?? throw new UnauthorizedAccessException("Invalid or expired MFA session");
        return passkeys.BeginAssertionAsync(userId, request.Origin, cancellationToken);
    }
}
