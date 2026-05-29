namespace Forge.Core.Models;

/// <summary>
/// Body of <c>POST /api/v1/auth/sso/exchange-code</c>. The browser SSO callback
/// redirects with an opaque single-use <c>code</c> (never the JWT); the SPA
/// posts that code back here to receive the real session. See
/// <c>ISsoHandoffStore</c> for why the credential is kept out of the URL.
/// </summary>
public record SsoExchangeCodeRequestModel
{
    /// <summary>Opaque single-use code from the SSO callback redirect.</summary>
    public string Code { get; init; } = string.Empty;
}
