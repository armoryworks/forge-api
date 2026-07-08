namespace Forge.Core.Models;

/// <summary>
/// Create/update payload for a single sales-order stage. On create the stage is
/// appended to the order; on update the addressed stage's fields are replaced.
/// </summary>
public record UpsertSalesOrderStageRequestModel(
    string Name,
    int Sequence,
    DateTimeOffset? PlannedProductionComplete,
    DateTimeOffset? PlannedShipDate,
    string? Notes,
    int? PaymentMilestoneId);
