using Forge.Api.Features.Auth;

namespace Forge.Api.Services;

/// <summary>
/// Single-use, short-lived handoff for the browser SSO flow. The OAuth
/// callback mints a Forge session, stashes the resulting
/// <see cref="LoginResponse"/> under an opaque random code, and redirects the
/// browser with only that code in the URL — never the JWT. The SPA then
/// exchanges the code (via POST) for the real <see cref="LoginResponse"/>.
///
/// This keeps the bearer credential out of the URL, where it would otherwise
/// leak into reverse-proxy / nginx access logs, the <c>Referer</c> header, and
/// browser history. The code itself is worthless after one consumption and
/// expires within seconds, so leaking it is non-exploitable.
///
/// In-memory by design, mirroring <see cref="SessionStore"/>: the codes are
/// consumed within seconds of issuance and a container restart simply forces
/// the (rare) in-flight SSO login to be retried.
/// </summary>
public interface ISsoHandoffStore
{
    /// <summary>Store the response under a fresh opaque code and return the code.</summary>
    string Create(LoginResponse response);

    /// <summary>
    /// Atomically remove and return the response for <paramref name="code"/>,
    /// or null if the code is unknown, already consumed, or expired.
    /// </summary>
    LoginResponse? Consume(string code);
}
