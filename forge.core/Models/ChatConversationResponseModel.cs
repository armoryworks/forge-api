namespace Forge.Core.Models;

public record ChatConversationResponseModel(
    int UserId,
    string UserName,
    string UserInitials,
    string UserColor,
    string? LastMessage,
    DateTimeOffset? LastMessageAt,
    int UnreadCount);
