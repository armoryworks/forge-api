using Forge.Core.Entities;

namespace Forge.Api.Features.Shipments;

/// <summary>
/// Resolves the hazmat class a shipment line carries onto its shipping paperwork, from the part's
/// compliance field (the sales-order line's part, else the shipment line's own part). Null when the
/// part has no hazmat class, so shops that never set one see unchanged documents.
/// </summary>
public static class ShipmentLineHazmat
{
    public static string? ClassFor(ShipmentLine line)
    {
        var hazmatClass = (line.SalesOrderLine?.Part ?? line.Part)?.HazmatClass;
        return string.IsNullOrWhiteSpace(hazmatClass) ? null : hazmatClass.Trim();
    }

    public static bool AnyHazmat(IEnumerable<ShipmentLine> lines) => lines.Any(l => ClassFor(l) is not null);
}
