namespace Forge.Api.Services;

/// <summary>
/// Single-use, short-lived token embedded in the RFID-relay setup PowerShell
/// instead of a full Forge JWT. The PS1 lives on the client filesystem
/// (typically `C:\Program Files\QB Engineer\RfidRelay\` after install) and is
/// reusable / mailable / backupable. Embedding the issuer's session JWT
/// would leave a working bearer credential at rest on every workstation
/// that ever ran the installer — a credential valid for the full JWT
/// lifetime (typically an hour) and scoped to <i>everything</i> the issuing
/// admin can do.
///
/// The download token is scoped to one endpoint
/// (<c>GET /api/v1/downloads/rfid-relay-via-token.zip</c>), valid for at most
/// one fetch, and auto-expires within minutes — long enough for the
/// PowerShell installer to actually pull the zip, short enough that leaving
/// the PS1 file on disk doesn't matter. The user-id binding is so the
/// download remains attributable in logs.
///
/// Mirrors <see cref="ISsoHandoffStore"/>'s shape — same pattern, different
/// payload.
/// </summary>
public interface IDownloadTokenStore
{
    /// <summary>Mint a fresh single-use token bound to the user who initiated the download.</summary>
    string Issue(int userId);

    /// <summary>
    /// Atomically remove and return the bound user-id if the token is
    /// known, unconsumed, and unexpired. Returns null on unknown / already-
    /// consumed / expired tokens.
    /// </summary>
    int? Consume(string token);
}
