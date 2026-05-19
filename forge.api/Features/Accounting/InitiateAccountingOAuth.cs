using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Forge.Core.Models;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Build the authorization URL for an accounting provider's OAuth handshake
/// and store the CSRF state token in the caller's session.
///
/// Subscriber-shape architecture: every place in Forge that wants to kick
/// off an accounting OAuth flow (admin Integrations dialog "Connect"
/// button, accounting-screen "Sync now" reconnect prompt, token-refresh-
/// failure recovery flow) dispatches this single command. Per-provider
/// URL-build logic lives behind one switch — adding a new OAuth provider
/// is one new case + per-provider options binding.
///
/// Companion: <see cref="CompleteAccountingOAuthCommand"/> handles the
/// callback side once the user has authorised at the provider.
/// </summary>
public record InitiateAccountingOAuthCommand(string Provider)
    : IRequest<InitiateAccountingOAuthResult>;

public record InitiateAccountingOAuthResult(
    string Provider,
    string AuthorizationUrl,
    string State);

public class InitiateAccountingOAuthHandler(
    IHttpContextAccessor httpContextAccessor,
    IOptions<QuickBooksOptions> qbOptions,
    IOptions<XeroOptions> xeroOptions,
    IOptions<FreshBooksOptions> freshBooksOptions,
    IOptions<SageOptions> sageOptions,
    IOptions<ZohoOptions> zohoOptions) : IRequestHandler<InitiateAccountingOAuthCommand, InitiateAccountingOAuthResult>
{
    public Task<InitiateAccountingOAuthResult> Handle(
        InitiateAccountingOAuthCommand request, CancellationToken cancellationToken)
    {
        var session = httpContextAccessor.HttpContext?.Session
            ?? throw new InvalidOperationException(
                "OAuth state requires HttpContext.Session — caller must run inside an HTTP request.");

        var state = Guid.NewGuid().ToString("N");

        // Per-provider URL build. All providers share the OAuth code-flow
        // shape — only the endpoint, scopes, and audience-specific extras
        // differ. The state token is stored under `{provider}_oauth_state`
        // and validated by CompleteAccountingOAuthCommand on callback.
        var url = request.Provider.ToLowerInvariant() switch
        {
            "quickbooks" => BuildQuickBooksUrl(state),
            "xero" => BuildXeroUrl(state),
            "freshbooks" => BuildFreshBooksUrl(state),
            "sage" => BuildSageUrl(state),
            "zoho" => BuildZohoUrl(state),
            _ => throw new InvalidOperationException(
                $"Provider '{request.Provider}' does not support OAuth or is not yet routed."),
        };

        session.SetString($"{request.Provider.ToLowerInvariant()}_oauth_state", state);

        return Task.FromResult(new InitiateAccountingOAuthResult(
            Provider: request.Provider.ToLowerInvariant(),
            AuthorizationUrl: url,
            State: state));
    }

    private string BuildQuickBooksUrl(string state)
    {
        var o = qbOptions.Value;
        if (string.IsNullOrEmpty(o.ClientId))
            throw new InvalidOperationException(
                "QuickBooks is not configured. Set ClientId + ClientSecret via the admin Integrations dialog.");

        return $"{o.AuthorizationEndpoint}" +
            $"?client_id={Uri.EscapeDataString(o.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(o.RedirectUri)}" +
            $"&response_type=code" +
            $"&scope={Uri.EscapeDataString(o.Scopes)}" +
            $"&state={state}";
    }

    private string BuildXeroUrl(string state)
    {
        var o = xeroOptions.Value;
        if (string.IsNullOrEmpty(o.ClientId))
            throw new InvalidOperationException(
                "Xero is not configured. Set ClientId + ClientSecret via the admin Integrations dialog.");

        return $"{o.AuthorizationEndpoint}" +
            $"?client_id={Uri.EscapeDataString(o.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(o.RedirectUri)}" +
            $"&response_type=code" +
            $"&scope={Uri.EscapeDataString(o.Scopes)}" +
            $"&state={state}";
    }

    private string BuildFreshBooksUrl(string state)
    {
        var o = freshBooksOptions.Value;
        if (string.IsNullOrEmpty(o.ClientId))
            throw new InvalidOperationException(
                "FreshBooks is not configured. Set ClientId + ClientSecret via the admin Integrations dialog.");

        return $"{o.AuthorizationEndpoint}" +
            $"?client_id={Uri.EscapeDataString(o.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(o.RedirectUri)}" +
            $"&response_type=code" +
            $"&state={state}";
    }

    private string BuildSageUrl(string state)
    {
        var o = sageOptions.Value;
        if (string.IsNullOrEmpty(o.ClientId))
            throw new InvalidOperationException(
                "Sage is not configured. Set ClientId + ClientSecret via the admin Integrations dialog.");

        return $"{o.AuthorizationEndpoint}" +
            $"?client_id={Uri.EscapeDataString(o.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(o.RedirectUri)}" +
            $"&response_type=code" +
            $"&scope=full_access" +
            $"&country={Uri.EscapeDataString(o.CountryCode)}" +
            $"&state={state}";
    }

    private string BuildZohoUrl(string state)
    {
        var o = zohoOptions.Value;
        if (string.IsNullOrEmpty(o.ClientId))
            throw new InvalidOperationException(
                "Zoho is not configured. Set ClientId + ClientSecret via the admin Integrations dialog.");

        return $"{o.AuthorizationEndpoint}" +
            $"?client_id={Uri.EscapeDataString(o.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(o.RedirectUri)}" +
            $"&response_type=code" +
            $"&scope={Uri.EscapeDataString(o.Scopes)}" +
            $"&access_type=offline" +
            $"&prompt=consent" +
            $"&state={state}";
    }
}
