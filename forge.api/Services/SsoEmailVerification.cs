namespace Forge.Api.Services;

/// <summary>
/// The one rule for whether an external identity's email may be trusted, shared
/// by the SSO token-exchange validator and the browser SSO callback. Email is the
/// auto-link key when no provider id is linked yet, so an unverified email must
/// never reach the account lookup. Google must assert <c>email_verified=true</c>;
/// Microsoft and generic OIDC are refused only when they assert it is false
/// (AAD organisational accounts and many IdPs omit the claim).
/// </summary>
public static class SsoEmailVerification
{
    public const string ClaimType = "email_verified";

    public static bool IsTrusted(string provider, string? emailVerified)
    {
        var verified = string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase);
        return string.Equals(provider, "google", StringComparison.OrdinalIgnoreCase)
            ? verified
            : emailVerified is null || verified;
    }
}
