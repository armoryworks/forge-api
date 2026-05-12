using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public interface IDashboardRepository
{
    Task<DashboardDataSet> GetDashboardDataAsync(CancellationToken ct);
}

public record DashboardDataSet(
    TrackType? ProductionTrack,
    List<Job> Jobs,
    Dictionary<int, ApplicationUserInfo> Users,
    List<JobActivityLog> RecentActivity);

public record ApplicationUserInfo(
    int Id, string? Initials, string? FirstName, string? LastName, string? AvatarColor);
