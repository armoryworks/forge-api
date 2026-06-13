namespace Forge.Core.Models;

/// <summary>⚡ EDI BOUNDARY — sanitized SFTP transport display (the password never leaves the server).</summary>
public record EdiSftpTransportInfoModel(
    string Host,
    int Port,
    string Username,
    bool HasPassword,
    string OutboundDir,
    string InboundDir);
