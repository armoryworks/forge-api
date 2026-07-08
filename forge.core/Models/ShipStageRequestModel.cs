namespace Forge.Core.Models;

/// <summary>
/// Ship-stage payload. When the stage has no linked shipment yet, an optional
/// <see cref="ShipmentId"/> may be supplied to link one; if the stage already has
/// a shipment the linkage is left untouched.
/// </summary>
public record ShipStageRequestModel(
    int? ShipmentId);
