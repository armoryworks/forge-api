using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SampleShipments;

public record UpdateSampleShipmentCommand(int Id, UpdateSampleShipmentRequest Request) : IRequest<SampleShipmentResponseModel>;

public class UpdateSampleShipmentHandler(AppDbContext db) : IRequestHandler<UpdateSampleShipmentCommand, SampleShipmentResponseModel>
{
    public async Task<SampleShipmentResponseModel> Handle(UpdateSampleShipmentCommand request, CancellationToken ct)
    {
        var s = await db.SampleShipments.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Sample shipment {request.Id} not found.");
        var r = request.Request;
        var prevStatus = s.Status;
        s.PartId = r.PartId;
        s.PartDescription = r.PartDescription?.Trim();
        s.Quantity = Math.Max(1, r.Quantity);
        s.Status = r.Status;
        s.RequestedAt = r.RequestedAt;
        s.ShippedAt = r.ShippedAt;
        s.DeliveredAt = r.DeliveredAt;
        s.CostToUs = r.CostToUs;
        s.ChargedAmount = r.ChargedAmount;
        s.TrackingNumber = r.TrackingNumber?.Trim();
        s.Carrier = r.Carrier?.Trim();
        s.Notes = r.Notes?.Trim();
        s.ShipToAddress = r.ShipToAddress?.Trim();
        s.FollowUpDate = r.FollowUpDate;
        if (prevStatus != s.Status)
            db.LogActivityAt("sample-status-changed", $"Sample shipment {prevStatus} → {s.Status}", SampleShipmentProjection.IndexingPoints(s));
        await db.SaveChangesAsync(ct);
        return SampleShipmentProjection.ToModel(s);
    }
}
