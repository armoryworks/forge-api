using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SampleShipments;

public record GetSampleShipmentsQuery(int? LeadId) : IRequest<List<SampleShipmentResponseModel>>;

public class GetSampleShipmentsHandler(AppDbContext db) : IRequestHandler<GetSampleShipmentsQuery, List<SampleShipmentResponseModel>>
{
    public async Task<List<SampleShipmentResponseModel>> Handle(GetSampleShipmentsQuery request, CancellationToken ct)
    {
        var q = db.SampleShipments.AsNoTracking().AsQueryable();
        if (request.LeadId.HasValue) q = q.Where(s => s.LeadId == request.LeadId.Value);
        var rows = await q.OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    private static SampleShipmentResponseModel Map(SampleShipment s) =>
        new(s.Id, s.LeadId, s.PartId, s.PartDescription, s.Quantity, s.Status, s.RequestedAt,
            s.ShippedAt, s.DeliveredAt, s.CostToUs, s.ChargedAmount, s.TrackingNumber, s.Carrier, s.Notes, s.CreatedAt);
}

public record CreateSampleShipmentCommand(CreateSampleShipmentRequest Request) : IRequest<SampleShipmentResponseModel>;

public class CreateSampleShipmentHandler(AppDbContext db) : IRequestHandler<CreateSampleShipmentCommand, SampleShipmentResponseModel>
{
    public async Task<SampleShipmentResponseModel> Handle(CreateSampleShipmentCommand request, CancellationToken ct)
    {
        var r = request.Request;
        var leadExists = await db.Leads.AnyAsync(l => l.Id == r.LeadId, ct);
        if (!leadExists) throw new KeyNotFoundException($"Lead {r.LeadId} not found.");
        var s = new SampleShipment
        {
            LeadId = r.LeadId,
            PartId = r.PartId,
            PartDescription = r.PartDescription?.Trim(),
            Quantity = Math.Max(1, r.Quantity),
            Status = "Requested",
            RequestedAt = DateTimeOffset.UtcNow,
            Notes = r.Notes?.Trim(),
        };
        db.SampleShipments.Add(s);
        db.LogActivityAt("sample-requested", $"Sample shipment requested ({s.Quantity}× {s.PartDescription ?? "part"})", ("Lead", r.LeadId));
        await db.SaveChangesAsync(ct);
        return new SampleShipmentResponseModel(s.Id, s.LeadId, s.PartId, s.PartDescription, s.Quantity, s.Status,
            s.RequestedAt, s.ShippedAt, s.DeliveredAt, s.CostToUs, s.ChargedAmount, s.TrackingNumber, s.Carrier, s.Notes, s.CreatedAt);
    }
}

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
        if (prevStatus != s.Status)
            db.LogActivityAt("sample-status-changed", $"Sample shipment {prevStatus} → {s.Status}", ("Lead", s.LeadId));
        await db.SaveChangesAsync(ct);
        return new SampleShipmentResponseModel(s.Id, s.LeadId, s.PartId, s.PartDescription, s.Quantity, s.Status,
            s.RequestedAt, s.ShippedAt, s.DeliveredAt, s.CostToUs, s.ChargedAmount, s.TrackingNumber, s.Carrier, s.Notes, s.CreatedAt);
    }
}
