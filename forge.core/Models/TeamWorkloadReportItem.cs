namespace Forge.Core.Models;

public record TeamWorkloadReportItem(
    int UserId,
    string UserName,
    string Initials,
    string AvatarColor,
    int ActiveJobs,
    int OverdueJobs,
    decimal HoursThisWeek);
