namespace Forge.Core.Models;

public record DashboardTaskResponseModel(
    int Id,
    string Time,
    string Title,
    string JobNumber,
    string BarColor,
    AssigneeInfo Assignee,
    string Status,
    string StatusColor);
