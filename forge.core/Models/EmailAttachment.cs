namespace Forge.Core.Models;

public record EmailAttachment(
    string FileName,
    string ContentType,
    byte[] Content);
