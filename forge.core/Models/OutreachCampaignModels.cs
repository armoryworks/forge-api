using Forge.Core.Enums;

namespace Forge.Core.Models;

public record OutreachCampaignResponseModel(
    int Id,
    string Name,
    string? Description,
    BulkLeadIntakeStrategy Strategy,
    int? DefaultCooldownDays,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    bool IsActive,
    int? OwnerUserId,
    int LeadCount,
    DateTimeOffset CreatedAt);

public record CreateOutreachCampaignRequest(
    string Name,
    string? Description,
    BulkLeadIntakeStrategy Strategy,
    int? DefaultCooldownDays,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt);

public record UpdateOutreachCampaignRequest(
    string Name,
    string? Description,
    int? DefaultCooldownDays,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    bool IsActive);
