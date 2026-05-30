using System.Collections.Concurrent;
using System.Security.Authentication;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// Consolidated external id_token validator. Handles all three providers the
/// SSO token-exchange endpoint supports — Google, Microsoft (Azure AD v2.0),
/// and a generic OIDC IdP configured per install. Uses the standard
/// <see cref="ConfigurationManager{T}"/> + <see cref="JsonWebTokenHandler"/>
/// stack against each provider's discovery document; no provider-specific
/// SDK dependency.
///
/// <para><b>Configuration cache.</b> The <see cref="ConfigurationManager{T}"/>
/// instances are kept in a process-wide dictionary keyed by authority URL so
/// the JWKS cache (default: 12h refresh, 5min refresh-on-error floor) is
/// shared across calls. Constructing per-call would defeat the cache and
/// hammer the provider on every validation.</para>
///
/// <para><b>Subject + email per provider.</b>
/// <list type="bullet">
///   <item><b>Google.</b> Subject = <c>sub</c>, email = <c>email</c>,
///     <c>email_verified=true</c> required, hosted-domain = <c>hd</c>.</item>
///   <item><b>Microsoft.</b> Subject = <c>oid</c> when present (the AAD
///     object id is stable across all OAuth clients in the tenant; using
///     <c>sub</c> would be per-app and break federation across apps that
///     share a Forge install). Falls back to <c>sub</c> for personal
///     accounts that don't carry <c>oid</c>. Email = <c>email</c> →
///     <c>preferred_username</c>.</item>
///   <item><b>OIDC.</b> Subject = <c>sub</c>, email = <c>email</c>;
///     <c>email_verified</c> respected when present (rejects if false) but
///     not required (many IdPs omit the claim).</item>
/// </list></para>
///
/// <para><b>Multi-audience.</b> Every validator accepts the configured
/// <see cref="SsoProviderOptions.ClientId"/> plus any entries in
/// <see cref="SsoProviderOptions.AdditionalAudiences"/>, so a federated
/// client (e.g. Tuyere holding its own Google OAuth client id) can trade
/// its id_token without Forge sharing OAuth credentials with it.</para>
/// </summary>
public class ExternalIdTokenValidator(IOptionsMonitor<SsoOptions> sso) : IExternalIdTokenValidator
{
    private const string GoogleAuthority = "https://accounts.google.com";
    private const string MicrosoftCommonAuthority = "https://login.microsoftonline.com/common/v2.0";

    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> ConfigManagers
        = new();
    private static readonly JsonWebTokenHandler Handler = new();

    /// <summary>
    /// Multi-tenant Microsoft issuers always shape as
    /// <c>https://login.microsoftonline.com/{tenant-guid}/v2.0</c>. A trailing
    /// slash is allowed by the spec; some token issuers emit it.
    /// </summary>
    private static readonly Regex MicrosoftMultiTenantIssuer = new(
        @"^https://login\.microsoftonline\.com/[0-9a-fA-F-]+/v2\.0/?$",
        RegexOptions.Compiled);

    public async Task<ExternalIdTokenClaims> ValidateGoogleAsync(string idToken, CancellationToken ct)
    {
        EnsureToken(idToken);
        var opts = sso.CurrentValue.Google;
        if (!opts.Enabled)
            throw new AuthenticationException("Google SSO is not enabled on this install.");
        if (string.IsNullOrWhiteSpace(opts.ClientId))
            throw new AuthenticationException("Google SSO client id is not configured.");

        var config = await GetConfigurationAsync(
            $"{GoogleAuthority}/.well-known/openid-configuration", "Google", ct);

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[] { GoogleAuthority, "accounts.google.com" },
            ValidateAudience = true,
            ValidAudiences = AudiencesFor(opts),
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = config.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        var result = await Handler.ValidateTokenAsync(idToken, parameters);
        if (!result.IsValid)
            throw new AuthenticationException("id_token validation failed.", result.Exception);

        var subject = result.ClaimsIdentity.FindFirst("sub")?.Value
            ?? throw new AuthenticationException("id_token missing 'sub' claim.");
        var email = result.ClaimsIdentity.FindFirst("email")?.Value
            ?? throw new AuthenticationException("id_token missing 'email' claim.");

        // Google sets email_verified; refuse unverified emails because email
        // is the fallback link in SsoCallback when no GoogleId exists yet.
        var emailVerified = result.ClaimsIdentity.FindFirst("email_verified")?.Value;
        if (!string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
            throw new AuthenticationException("id_token email is not verified by Google.");

        var hostedDomain = result.ClaimsIdentity.FindFirst("hd")?.Value;
        return new ExternalIdTokenClaims(subject, email, hostedDomain);
    }

    public async Task<ExternalIdTokenClaims> ValidateMicrosoftAsync(string idToken, CancellationToken ct)
    {
        EnsureToken(idToken);
        var opts = sso.CurrentValue.Microsoft;
        if (!opts.Enabled)
            throw new AuthenticationException("Microsoft SSO is not enabled on this install.");
        if (string.IsNullOrWhiteSpace(opts.ClientId))
            throw new AuthenticationException("Microsoft SSO client id is not configured.");

        var authority = string.IsNullOrWhiteSpace(opts.Authority)
            ? MicrosoftCommonAuthority
            : opts.Authority.TrimEnd('/');
        // "common" / "organizations" / "consumers" all behave as multi-tenant
        // for validation purposes — the discovery doc carries a templated
        // issuer like <c>https://login.microsoftonline.com/{tenantid}/v2.0</c>
        // that won't match the actual token issuer verbatim.
        var isMultiTenant =
            authority.EndsWith("/common/v2.0", StringComparison.OrdinalIgnoreCase) ||
            authority.EndsWith("/common", StringComparison.OrdinalIgnoreCase) ||
            authority.EndsWith("/organizations/v2.0", StringComparison.OrdinalIgnoreCase) ||
            authority.EndsWith("/organizations", StringComparison.OrdinalIgnoreCase) ||
            authority.EndsWith("/consumers/v2.0", StringComparison.OrdinalIgnoreCase) ||
            authority.EndsWith("/consumers", StringComparison.OrdinalIgnoreCase);

        var config = await GetConfigurationAsync(
            $"{authority}/.well-known/openid-configuration", "Microsoft", ct);

        var parameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudiences = AudiencesFor(opts),
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = config.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(2),
            ValidateIssuer = true,
        };
        if (isMultiTenant)
        {
            parameters.IssuerValidator = (issuer, _, _) =>
            {
                if (issuer is not null && MicrosoftMultiTenantIssuer.IsMatch(issuer))
                    return issuer;
                throw new SecurityTokenInvalidIssuerException(
                    $"Microsoft issuer '{issuer}' does not match the expected " +
                    "<c>https://login.microsoftonline.com/{tenant}/v2.0</c> shape.");
            };
        }
        else
        {
            parameters.ValidIssuer = config.Issuer;
        }

        var result = await Handler.ValidateTokenAsync(idToken, parameters);
        if (!result.IsValid)
            throw new AuthenticationException("id_token validation failed.", result.Exception);

        // Optional Sso:Microsoft:AllowedTenantIds gate. When configured, the
        // token's `tid` claim must be in the list. This complements the
        // multi-tenant validator above: the install stays multi-tenant on the
        // authority side (no Authority override needed) while still
        // restricting trust to a known set of customer tenants. Empty / null
        // list = no per-tenant restriction.
        if (opts.AllowedTenantIds is { Count: > 0 } allowedTenants)
        {
            var tid = result.ClaimsIdentity.FindFirst("tid")?.Value;
            if (string.IsNullOrEmpty(tid))
                throw new AuthenticationException(
                    "Microsoft id_token missing 'tid' claim — required when AllowedTenantIds is configured.");
            if (!allowedTenants.Any(t => string.Equals(t, tid, StringComparison.OrdinalIgnoreCase)))
                throw new AuthenticationException(
                    $"Microsoft tenant '{tid}' is not permitted on this install.");
        }

        // Prefer `oid` (AAD object id — stable across every OAuth client in
        // the tenant) so a federated client with its own client id still
        // matches the same Forge user via MicrosoftId. `sub` is per-app and
        // would silently fork the identity. Personal MS accounts omit `oid`
        // — fall back to `sub` there.
        var subject = result.ClaimsIdentity.FindFirst("oid")?.Value
            ?? result.ClaimsIdentity.FindFirst("sub")?.Value
            ?? throw new AuthenticationException("Microsoft id_token missing both 'oid' and 'sub' claims.");
        var email = result.ClaimsIdentity.FindFirst("email")?.Value
            ?? result.ClaimsIdentity.FindFirst("preferred_username")?.Value
            ?? throw new AuthenticationException("Microsoft id_token missing 'email' / 'preferred_username' claim.");
        // Microsoft AAD organizational accounts are pre-verified at provisioning
        // and don't emit `email_verified`; respect it only if present.
        var emailVerified = result.ClaimsIdentity.FindFirst("email_verified")?.Value;
        if (emailVerified is not null
            && !string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
            throw new AuthenticationException("Microsoft id_token email is not verified.");

        return new ExternalIdTokenClaims(subject, email, HostedDomain: null);
    }

    public async Task<ExternalIdTokenClaims> ValidateOidcAsync(string idToken, CancellationToken ct)
    {
        EnsureToken(idToken);
        var opts = sso.CurrentValue.Oidc;
        if (!opts.Enabled)
            throw new AuthenticationException("OIDC SSO is not enabled on this install.");
        if (string.IsNullOrWhiteSpace(opts.Authority))
            throw new AuthenticationException("OIDC SSO authority is not configured.");
        if (string.IsNullOrWhiteSpace(opts.ClientId))
            throw new AuthenticationException("OIDC SSO client id is not configured.");

        var authority = opts.Authority.TrimEnd('/');
        var config = await GetConfigurationAsync(
            $"{authority}/.well-known/openid-configuration", "OIDC", ct);

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = config.Issuer,
            ValidateAudience = true,
            ValidAudiences = AudiencesFor(opts),
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = config.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        var result = await Handler.ValidateTokenAsync(idToken, parameters);
        if (!result.IsValid)
            throw new AuthenticationException("id_token validation failed.", result.Exception);

        var subject = result.ClaimsIdentity.FindFirst("sub")?.Value
            ?? throw new AuthenticationException("OIDC id_token missing 'sub' claim.");
        var email = result.ClaimsIdentity.FindFirst("email")?.Value
            ?? throw new AuthenticationException("OIDC id_token missing 'email' claim.");
        // OIDC: respect email_verified if present, accept otherwise.
        var emailVerified = result.ClaimsIdentity.FindFirst("email_verified")?.Value;
        if (emailVerified is not null
            && !string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
            throw new AuthenticationException("OIDC id_token email is not verified.");

        return new ExternalIdTokenClaims(subject, email, HostedDomain: null);
    }

    private static void EnsureToken(string idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new AuthenticationException("id_token is required.");
    }

    private static IEnumerable<string> AudiencesFor(SsoProviderOptions opts)
    {
        yield return opts.ClientId;
        if (opts.AdditionalAudiences is null) yield break;
        foreach (var a in opts.AdditionalAudiences)
            if (!string.IsNullOrWhiteSpace(a)) yield return a;
    }

    private static async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string discoveryUrl, string providerLabel, CancellationToken ct)
    {
        var cm = ConfigManagers.GetOrAdd(discoveryUrl, url =>
            new ConfigurationManager<OpenIdConnectConfiguration>(
                url, new OpenIdConnectConfigurationRetriever()));
        try
        {
            return await cm.GetConfigurationAsync(ct);
        }
        catch (Exception ex)
        {
            // JWKS / discovery fetch failure — treat as a server-side outage,
            // not a bad token. Surface as auth failure (the caller can't
            // recover) but include the inner detail for log triage.
            throw new AuthenticationException(
                $"Failed to fetch {providerLabel} OIDC configuration.", ex);
        }
    }
}
