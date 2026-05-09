namespace QBEngineer.Core.Models.Communications;

/// <summary>
/// Wave 8 — JSON shape stored in <c>CommunicationSyncConfig.ConfigJson</c>
/// when ProviderId="imap". Non-secret connection settings (host, port,
/// SSL, mailbox folder) live here; the password lives in
/// <c>CommunicationSyncConfig.AccessToken</c> as a sealed envelope
/// (Data Protection API).
///
/// Defaults match Gmail's IMAP settings since that's the most common
/// case (imap.gmail.com:993 SSL, INBOX folder). Outlook / Yahoo / custom
/// servers override host + port.
/// </summary>
public class ImapConnectionConfig
{
    public string Host { get; set; } = "imap.gmail.com";
    public int Port { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    /// <summary>Mailbox folder to poll. Default INBOX; advanced users
    /// may point at a Gmail label like "[Gmail]/All Mail".</summary>
    public string Mailbox { get; set; } = "INBOX";
}
