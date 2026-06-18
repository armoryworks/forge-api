namespace Forge.Core.Models;

public record CarrierListItemModel(
    int Id,
    string Name,
    string? Code,
    string? Scac,
    string IntegrationKind,
    string DeliveryUpdateMode,
    string? IntegrationServiceId,
    bool RequiresScanToShip,
    bool IsActive,
    int SortOrder);
