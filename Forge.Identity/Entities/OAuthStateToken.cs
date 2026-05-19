namespace Forge.Identity.Entities;

/// <summary>
/// Wave 8 phase 1k.2 — short-lived state token tying an in-flight OAuth
/// authorize request back to the user who initiated it. Mitigates CSRF
/// (an attacker can't forge a callback that lands in someone else's
/// account) and binds the eventual <c>code</c> exchange to the
/// originating user without trusting the redirect URL.
///
/// Lifecycle:
///   1. User clicks "Connect Gmail" → server generates a random token,
///      persists this row with the user id + provider key, returns the
///      authorize URL with <c>state=token</c>.
///   2. User completes the OAuth round-trip; provider redirects to the
///      callback with the same <c>state</c> echoed back.
///   3. Server looks up the row by token, verifies <c>UserId</c> matches
///      the calling user, exchanges <c>code</c> for tokens, deletes the
///      row.
///
/// Tokens auto-expire 10 min after creation. A daily cleanup job (or
/// the background sweep on the next callback) removes orphans from
/// abandoned flows.
/// </summary>
public class OAuthStateToken : BaseEntity
{
    /// <summary>URL-safe random string. 256 bits of entropy is plenty;
    /// we store hex-encoded so the cookie / URL parameter stays ASCII.</summary>
    public string Token { get; set; } = string.Empty;

    public int UserId { get; set; }

    /// <summary>Provider key from <see cref="Forge.Core.Models.Communications.ImapOAuthProvider.Key"/>
    /// — "google" / "microsoft". Captured at begin-time so the callback
    /// route is canonical even if the user's clipboard / link gets
    /// crossed between providers.</summary>
    public string ProviderKey { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
}
