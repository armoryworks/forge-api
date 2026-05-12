namespace Forge.Core.Models;

public record ShopFloorWorkerResponseModel(
    int UserId,
    string Name,
    string Initials,
    string AvatarColor,
    string? CurrentTask,
    int? CurrentJobId,
    string? CurrentJobNumber,
    string TimeOnTask);
