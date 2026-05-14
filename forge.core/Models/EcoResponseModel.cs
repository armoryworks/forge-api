
namespace Forge.Core.Models;

public record EcoResponseModel
{
    public int Id { get; init; }
    public string EcoNumber { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public EcoChangeType ChangeType { get; init; }
    public EcoStatus Status { get; init; }
    public EcoPriority Priority { get; init; }
    public string? ReasonForChange { get; init; }
    public string? ImpactAnalysis { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public int RequestedById { get; init; }
    public string RequestedByName { get; init; } = string.Empty;
    public string? ApprovedByName { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public DateTimeOffset? ImplementedAt { get; init; }
    public int AffectedItemCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public List<EcoAffectedItemResponseModel> AffectedItems { get; init; } = [];
}

public record EcoAffectedItemResponseModel
{
    public int Id { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public int EntityId { get; init; }
    public string ChangeDescription { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public bool IsImplemented { get; init; }
}
