namespace Forge.Core.Models;

/// <summary>L2: correctable fields on an existing lot (expiry / supplier-lot / notes).</summary>
public record UpdateLotRecordRequestModel(
    DateTimeOffset? ExpirationDate,
    string? SupplierLotNumber,
    string? Notes);
