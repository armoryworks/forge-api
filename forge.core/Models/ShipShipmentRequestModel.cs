namespace Forge.Core.Models;

/// <summary>
/// Optional body for POST /shipments/{id}/ship. Carries the scanned label value for the
/// scan-to-ship gate; an empty body is valid for shipments whose carrier doesn't require a scan.
/// </summary>
public record ShipShipmentRequestModel(string? ScanCode);
