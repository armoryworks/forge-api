namespace Forge.Core.Models;

/// <summary>
/// ⚡ EDI BOUNDARY — typed SFTP transport fields from the admin partner dialog (never raw JSON).
/// A null/blank <paramref name="Password"/> on update keeps the existing stored password.
/// </summary>
public record EdiSftpTransportRequestModel(
    string Host,
    int Port,
    string Username,
    string? Password,
    string OutboundDir,
    string InboundDir);
