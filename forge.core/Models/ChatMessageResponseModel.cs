namespace Forge.Core.Models;

public record ChatMessageResponseModel(
    int Id,
    int SenderId,
    string SenderName,
    string SenderInitials,
    string SenderColor,
    int RecipientId,
    string Content,
    bool IsRead,
    DateTimeOffset CreatedAt,
    int? ParentMessageId = null,
    int ThreadReplyCount = 0,
    DateTimeOffset? ThreadLastReplyAt = null,
    List<ChatMessageMentionResponseModel>? Mentions = null,
    ChatFileAttachmentResponseModel? FileAttachment = null,
    string? LinkedEntityType = null,
    int? LinkedEntityId = null);

public record ChatFileAttachmentResponseModel(
    int Id,
    string FileName,
    string ContentType,
    long Size);

public record ChatMessageMentionResponseModel(
    string EntityType,
    int EntityId,
    string DisplayText);
