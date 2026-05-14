
namespace Forge.Core.Models;

public record AnnouncementResponseModel(
    int Id,
    string Title,
    string Content,
    AnnouncementSeverity Severity,
    AnnouncementScope Scope,
    bool RequiresAcknowledgment,
    DateTimeOffset? ExpiresAt,
    bool IsSystemGenerated,
    string? SystemSource,
    int CreatedById,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    int AcknowledgmentCount,
    int TargetUserCount,
    bool IsAcknowledgedByCurrentUser,
    List<int> TargetTeamIds);
