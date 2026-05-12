namespace Forge.Core.Models;

public record ScheduleMilestoneModel(
    int SalesOrderLineId,
    string? PartNumber,
    string? PartDescription,
    DateTimeOffset? DeliveryDate,
    DateTimeOffset? ShipBy,
    DateTimeOffset? QcCompleteBy,
    DateTimeOffset? ProductionCompleteBy,
    DateTimeOffset? ProductionStartBy,
    DateTimeOffset? MaterialsNeededBy,
    DateTimeOffset? PoOrderBy,
    bool IsAtRisk);
