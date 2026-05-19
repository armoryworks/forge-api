using System.Security.Authentication;

using FluentValidation;
using MediatR;

using Forge.Api.Services;

namespace Forge.Api.Features.Auth;

/// <summary>
/// Trades an external-provider id_token (today: Google) for a Forge JWT.
/// Used by federated client apps that already authenticated the user via
/// the same IdP and want to drive Forge actions AS that user without going
/// through the browser-based OAuth code flow.
///
/// Flow:
///   1. <see cref="IExternalIdTokenValidator"/> validates the id_token
///      (signature, issuer, audience, lifetime, email_verified).
///   2. The validated subject + email are passed to
///      <see cref="SsoCallbackCommand"/>, reusing the same user lookup,
///      auto-link, JWT issuance, and session-creation logic that the
///      browser flow uses.
///   3. The returned <see cref="LoginResponse"/> carries the Forge JWT —
///      the client caches it and uses the standard JwtBearer scheme for
///      subsequent calls.
///
/// Errors:
///   - id_token rejected (bad signature, expired, wrong audience, etc.) →
///     <see cref="AuthenticationException"/> → 401 problem+json.
///   - No matching ApplicationUser → <see cref="InvalidOperationException"/>
///     from <see cref="SsoCallbackHandler"/> → 404 / 401 per middleware
///     mapping.
/// </summary>
public record ExchangeSsoTokenCommand(string Provider, string IdToken)
    : IRequest<LoginResponse>;

public class ExchangeSsoTokenValidator : AbstractValidator<ExchangeSsoTokenCommand>
{
    public ExchangeSsoTokenValidator()
    {
        RuleFor(x => x.Provider).NotEmpty()
            .Must(p => string.Equals(p, "google", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only the 'google' provider is supported today.");
        RuleFor(x => x.IdToken).NotEmpty().MinimumLength(20);
    }
}

public class ExchangeSsoTokenHandler(
    IExternalIdTokenValidator validator,
    IMediator mediator) : IRequestHandler<ExchangeSsoTokenCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(
        ExchangeSsoTokenCommand request, CancellationToken cancellationToken)
    {
        // Provider validation already enforced "google" via FluentValidation;
        // when additional providers are added, dispatch on Provider here.
        var claims = await validator.ValidateGoogleAsync(request.IdToken, cancellationToken);

        // Reuse the browser-flow callback. It owns user lookup (by subject
        // then by email), the auto-link path, AllowedDomains enforcement
        // (Task 4), Forge JWT issuance, and SessionStore creation. Keeps
        // the two flows in lockstep — a change to user resolution affects
        // both, with no parallel duplication to drift.
        return await mediator.Send(
            new SsoCallbackCommand(
                Provider: request.Provider.ToLowerInvariant(),
                ExternalId: claims.Subject,
                Email: claims.Email),
            cancellationToken);
    }
}
