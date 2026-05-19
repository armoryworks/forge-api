using System.Security.Authentication;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;

using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// Validates Google-issued OIDC id_tokens against Google's published JWKS
/// (<c>https://www.googleapis.com/oauth2/v3/certs</c>) and the configured
/// Google client id (audience). Implementation uses the standard Microsoft
/// <see cref="ConfigurationManager{T}"/> + <see cref="JsonWebTokenHandler"/>
/// stack — no Google-specific SDK dependency.
///
/// The signing-key set is cached and auto-refreshed by
/// <c>ConfigurationManager</c> (defaults: 12h refresh, 5min refresh-on-error
/// floor). Lifetime, issuer, and audience are validated; signature is
/// verified against the live JWKS.
/// </summary>
public class GoogleIdTokenValidator(IOptionsMonitor<SsoOptions> sso)
    : IExternalIdTokenValidator
{
    // Singleton-scoped — the ConfigurationManager owns its own HttpClient
    // and key cache; constructing one per validation defeats the cache.
    private static readonly ConfigurationManager<OpenIdConnectConfiguration> ConfigManager =
        new("https://accounts.google.com/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

    private static readonly JsonWebTokenHandler Handler = new();

    public async Task<ExternalIdTokenClaims> ValidateGoogleAsync(
        string idToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new AuthenticationException("id_token is required.");

        var googleOptions = sso.CurrentValue.Google;
        if (!googleOptions.Enabled)
            throw new AuthenticationException(
                "Google SSO is not enabled on this install.");
        if (string.IsNullOrWhiteSpace(googleOptions.ClientId))
            throw new AuthenticationException(
                "Google SSO client id is not configured.");

        OpenIdConnectConfiguration config;
        try
        {
            config = await ConfigManager.GetConfigurationAsync(ct);
        }
        catch (Exception ex)
        {
            // JWKS fetch failure — treat as a server-side outage, not a
            // bad-token. Surface as auth failure (the caller can't recover)
            // but include the inner detail for log triage.
            throw new AuthenticationException(
                "Failed to fetch Google's OIDC configuration.", ex);
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
            ValidateAudience = true,
            ValidAudience = googleOptions.ClientId,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = config.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        var result = await Handler.ValidateTokenAsync(idToken, validationParameters);
        if (!result.IsValid)
        {
            throw new AuthenticationException(
                "id_token validation failed.", result.Exception);
        }

        // Extract trusted claims.
        var subject = result.ClaimsIdentity.FindFirst("sub")?.Value
            ?? throw new AuthenticationException("id_token missing 'sub' claim.");
        var email = result.ClaimsIdentity.FindFirst("email")?.Value
            ?? throw new AuthenticationException("id_token missing 'email' claim.");

        // Google sets the `email_verified` claim — refuse unverified emails
        // since email is the fallback link in SsoCallback when no GoogleId
        // exists on the local user.
        var emailVerified = result.ClaimsIdentity.FindFirst("email_verified")?.Value;
        if (!string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
            throw new AuthenticationException(
                "id_token email is not verified by Google.");

        var hostedDomain = result.ClaimsIdentity.FindFirst("hd")?.Value;
        return new ExternalIdTokenClaims(subject, email, hostedDomain);
    }
}
