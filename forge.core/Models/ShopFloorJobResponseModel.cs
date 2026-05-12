namespace Forge.Core.Models;

public record ShopFloorJobResponseModel(
    int Id,
    string JobNumber,
    string Title,
    string StageName,
    string StageColor,
    string PriorityName,
    int? AssigneeId,
    string? AssigneeInitials,
    string? AssigneeColor,
    string? DueDate,
    bool IsOverdue);
