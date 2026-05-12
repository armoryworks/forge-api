namespace Forge.Core.Models;

public record TimeByUserReportItem(
    int UserId,
    string UserName,
    decimal TotalHours);
