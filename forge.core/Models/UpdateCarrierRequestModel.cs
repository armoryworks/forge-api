namespace Forge.Core.Models;

/// <summary>
/// Body for PUT /carriers/{id}. Every field is optional: a null field is left unchanged, and an empty
/// Code, Scac or Notes clears it. Credentials are set separately through PUT /carriers/{id}/credentials.
/// </summary>
public record UpdateCarrierRequestModel(
    string? Name = null,
    string? Code = null,
    string? Scac = null,
    string? IntegrationKind = null,
    string? DeliveryUpdateMode = null,
    string? IntegrationServiceId = null,
    bool? RequiresScanToShip = null,
    bool? IsActive = null,
    int? SortOrder = null,
    string? Notes = null);
