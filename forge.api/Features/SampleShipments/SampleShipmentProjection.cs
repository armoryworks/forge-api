using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.SampleShipments;

internal static class SampleShipmentProjection
{
    public static SampleShipmentResponseModel ToModel(SampleShipment s) =>
        new(s.Id, s.LeadId, s.PartId, s.PartDescription, s.Quantity, s.Status, s.RequestedAt,
            s.ShippedAt, s.DeliveredAt, s.CostToUs, s.ChargedAmount, s.TrackingNumber, s.Carrier, s.Notes, s.CreatedAt,
            s.CustomerId, s.ShipToAddress, s.FollowUpDate);

    public static (string EntityType, int EntityId)[] IndexingPoints(SampleShipment s)
    {
        var points = new List<(string, int)>();
        if (s.LeadId is int leadId) points.Add(("Lead", leadId));
        if (s.CustomerId is int customerId) points.Add(("Customer", customerId));
        return [.. points];
    }
}
