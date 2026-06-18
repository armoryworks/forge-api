namespace Forge.Core.Models;

public record CreateCarrierRequestModel(
    string Name,
    string? Code = null,
    string? Scac = null,
    string IntegrationKind = "Manual",
    string DeliveryUpdateMode = "Manual",
    string? IntegrationServiceId = null,
    bool RequiresScanToShip = true,
    string? Notes = null);
