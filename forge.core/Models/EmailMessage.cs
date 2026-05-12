namespace Forge.Core.Models;

public record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? PlainTextBody = null,
    List<EmailAttachment>? Attachments = null);
