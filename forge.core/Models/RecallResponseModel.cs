using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>Recall summary row (list view).</summary>
public record RecallResponseModel(
    int Id,
    int InitiatedLotId,
    string InitiatedLotNumber,
    string Reason,
    DateTimeOffset RecallDate,
    RecallStatus Status,
    int AffectedLotsCount,
    int AffectedShipmentsCount,
    decimal TotalQuarantinedQuantity,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt);
