namespace Forge.Core.Models;

public record AnnouncementAcknowledgmentResponseModel(
    int UserId,
    string UserName,
    DateTimeOffset AcknowledgedAt);
