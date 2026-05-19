namespace Forge.Core.Models;

/// <summary>
/// Body of <c>POST /api/v1/auth/sso/token-exchange</c>. The id_token is the
/// caller's credential — they hold a valid external-provider id_token and
/// want a Forge JWT in exchange, no browser redirect required. See
/// <c>docs/api-key-integrations.md</c> for the consumer contract.
/// </summary>
public record SsoTokenExchangeRequestModel
{
    /// <summary>Lowercase provider key. Today: <c>"google"</c> only.</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>The raw external-provider id_token (JWT, three dot-separated parts).</summary>
    public string IdToken { get; init; } = string.Empty;
}
