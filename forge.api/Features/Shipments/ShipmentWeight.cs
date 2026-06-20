using Forge.Core.Entities;

namespace Forge.Api.Features.Shipments;

public static class ShipmentWeight
{
    /// <summary>
    /// Package weight derived from the shipment's line parts (Σ quantity × part weight-each), or null
    /// when no line part carries a weight. Used as the rate/label fallback so the carrier sees a real
    /// ACTWGT instead of the 1 lb placeholder.
    /// </summary>
    public static decimal? Derive(Shipment shipment)
    {
        decimal total = 0m;
        var any = false;
        foreach (var line in shipment.Lines)
        {
            if (line.Part?.WeightEach is decimal each && each > 0m)
            {
                total += line.Quantity * each;
                any = true;
            }
        }
        return any ? total : null;
    }
}
