namespace Forge.Core.Models;

public record UpdateNotificationRequestModel(
    bool? IsRead,
    bool? IsPinned,
    bool? IsDismissed);
