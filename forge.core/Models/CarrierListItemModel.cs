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
    int SortOrder,
    // Credential status — never the secret. ClientId/environment are identifiers shown so the admin can
    // see what's configured; CredentialsConfigured drives the "configured ✓" badge.
    bool CredentialsConfigured = false,
    string? CredentialClientId = null,
    string? CredentialEnvironment = null);
