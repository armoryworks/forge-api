namespace Forge.Core.Models;

/// <summary>
/// Body for POST /shipments/{id}/label. CarrierId is optional: when omitted, the label is created
/// against the shipment's assigned carrier (its integration service id). Supply it to override.
/// </summary>
public record CreateShippingLabelRequestModel(string? CarrierId = null);
