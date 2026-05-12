namespace Forge.Core.Models.Communications;

/// <summary>
/// Wave 8 phase 1k.2 — static descriptor for one OAuth-IMAP provider
/// (Gmail / Microsoft 365). Holds the protocol-stable bits — authorize +
/// token URLs, scope strings, IMAP host — that don't change between
/// installs. Per-install ClientId/ClientSecret come from
/// <see cref="OAuthImapOptions"/>.
/// </summary>
public sealed record ImapOAuthProvider(
    string Key,
    string DisplayName,
    string AuthorizeUrl,
    string TokenUrl,
    string Scope,
    string ImapHost,
    int ImapPort = 993)
{
    public static readonly ImapOAuthProvider Google = new(
        Key: "google",
        DisplayName: "Gmail / Google Workspace",
        AuthorizeUrl: "https://accounts.google.com/o/oauth2/v2/auth",
        TokenUrl: "https://oauth2.googleapis.com/token",
        // Full mailbox access via IMAP. https://mail.google.com/ is the
        // documented Gmail-IMAP scope; .readonly is too narrow for full
        // matcher functionality.
        Scope: "https://mail.google.com/",
        ImapHost: "imap.gmail.com");

    public static readonly ImapOAuthProvider Microsoft = new(
        Key: "microsoft",
        DisplayName: "Outlook / Microsoft 365",
        // v2.0 endpoint with `common` tenant — accepts both work/school
        // (organizational) accounts and personal MSA accounts.
        AuthorizeUrl: "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
        TokenUrl: "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        // IMAP.AccessAsUser.All for Outlook IMAP; offline_access for the
        // refresh_token; openid+profile+email satisfy basic-profile claims
        // some MS tenants require for consent.
        Scope: "https://outlook.office.com/IMAP.AccessAsUser.All offline_access openid profile email",
        ImapHost: "outlook.office365.com");

    /// <summary>Lookup by short-key. Returns null for unknown providers.</summary>
    public static ImapOAuthProvider? FromKey(string? key) => key?.ToLowerInvariant() switch
    {
        "google" => Google,
        "microsoft" => Microsoft,
        _ => null,
    };

    public static IReadOnlyList<ImapOAuthProvider> All { get; } = [Google, Microsoft];
}
