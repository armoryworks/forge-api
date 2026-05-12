namespace Forge.Core.Models;

public record SendChatMessageRequestModel(
    int RecipientId,
    string Content,
    int? FileAttachmentId = null,
    string? LinkedEntityType = null,
    int? LinkedEntityId = null);
