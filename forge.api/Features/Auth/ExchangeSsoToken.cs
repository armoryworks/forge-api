using System.Security.Authentication;

using FluentValidation;
using MediatR;

using Forge.Api.Services;

namespace Forge.Api.Features.Auth;

/// <summary>
/// Trades an external-provider id_token for a Forge JWT. Used by federated
/// client apps that already authenticated the user via the same IdP and
/// want to drive Forge actions AS that user without going through the
/// browser-based OAuth code flow.
///
/// Supported providers (each gated independently via <c>Sso:&lt;Provider&gt;:Enabled</c>):
///   <list type="bullet">
///     <item><c>google</c> — Google id_token (workspace or personal).</item>
///     <item><c>microsoft</c> — Azure AD v2.0 id_token, multi-tenant by
///       default or single-tenant when <c>Sso:Microsoft:Authority</c> is set.</item>
///     <item><c>oidc</c> — Generic OIDC IdP configured via
///       <c>Sso:Oidc:Authority</c> (Okta, Auth0, Keycloak, custom).</item>
///   </list>
///
/// Flow:
///   1. <see cref="IExternalIdTokenValidator"/> validates the id_token
///      (signature, issuer, audience including any AdditionalAudiences,
///      lifetime, and email verification where the provider emits it).
///   2. The validated subject + email are passed to
///      <see cref="SsoCallbackCommand"/>, reusing the same user lookup,
///      auto-link, AllowedDomains enforcement, JWT issuance, and
///      session-creation logic that the browser flow uses. Keeping a
///      single resolution path means a change in user matching affects
///      both flows with no parallel duplication to drift.
///   3. The returned <see cref="LoginResponse"/> carries the Forge JWT —
///      the client caches it and uses the standard JwtBearer scheme for
///      subsequent calls.
///
/// Errors:
///   - id_token rejected (bad signature, expired, wrong audience,
///     unverified email) → <see cref="AuthenticationException"/> → 401.
///   - Provider disabled or misconfigured on this install →
///     <see cref="AuthenticationException"/> → 401.
///   - No matching ApplicationUser → <see cref="InvalidOperationException"/>
///     from <see cref="SsoCallbackHandler"/> → 404 / 401 per middleware
///     mapping.
/// </summary>
public record ExchangeSsoTokenCommand(string Provider, string IdToken)
    : IRequest<LoginResponse>;

public class ExchangeSsoTokenValidator : AbstractValidator<ExchangeSsoTokenCommand>
{
    private static readonly string[] SupportedProviders = ["google", "microsoft", "oidc"];

    public ExchangeSsoTokenValidator()
    {
        RuleFor(x => x.Provider).NotEmpty()
            .Must(p => SupportedProviders.Contains(p, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Provider must be one of: {string.Join(", ", SupportedProviders)}.");
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
        var provider = request.Provider.ToLowerInvariant();
        var claims = provider switch
        {
            "google" => await validator.ValidateGoogleAsync(request.IdToken, cancellationToken),
            "microsoft" => await validator.ValidateMicrosoftAsync(request.IdToken, cancellationToken),
            "oidc" => await validator.ValidateOidcAsync(request.IdToken, cancellationToken),
            // Defence-in-depth: FluentValidation already rejected unknown
            // providers, but keep the dispatch total so an upstream bypass
            // can't reach the SsoCallback with an unvalidated principal.
            _ => throw new AuthenticationException($"Unsupported SSO provider: {provider}."),
        };

        return await mediator.Send(
            new SsoCallbackCommand(
                Provider: provider,
                ExternalId: claims.Subject,
                Email: claims.Email),
            cancellationToken);
    }
}
