using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SampleShipments;

public record CreateSampleShipmentCommand(CreateSampleShipmentRequest Request) : IRequest<SampleShipmentResponseModel>;

public class CreateSampleShipmentValidator : AbstractValidator<CreateSampleShipmentCommand>
{
    public CreateSampleShipmentValidator()
    {
        RuleFor(x => x.Request)
            .Must(r => r.LeadId.HasValue || r.CustomerId.HasValue)
            .WithMessage("A sample goes to a lead or a customer. Pick one.");
        RuleFor(x => x.Request.Quantity).GreaterThan(0);
        RuleFor(x => x.Request.PartDescription).MaximumLength(500);
        RuleFor(x => x.Request.PartId)
            .NotNull()
            .When(x => string.IsNullOrWhiteSpace(x.Request.PartDescription))
            .WithMessage("Pick a part or describe the sample.");
        RuleFor(x => x.Request.ShipToAddress).MaximumLength(1000);
        RuleFor(x => x.Request.Carrier).MaximumLength(50);
        RuleFor(x => x.Request.TrackingNumber).MaximumLength(100);
        RuleFor(x => x.Request.Notes).MaximumLength(2000);
    }
}

public class CreateSampleShipmentHandler(AppDbContext db, IClock clock) : IRequestHandler<CreateSampleShipmentCommand, SampleShipmentResponseModel>
{
    public async Task<SampleShipmentResponseModel> Handle(CreateSampleShipmentCommand request, CancellationToken ct)
    {
        var r = request.Request;
        if (r.LeadId is int leadId && !await db.Leads.AnyAsync(l => l.Id == leadId, ct))
            throw new KeyNotFoundException($"Lead {leadId} not found.");
        if (r.CustomerId is int customerId && !await db.Customers.AnyAsync(c => c.Id == customerId, ct))
            throw new KeyNotFoundException($"Customer {customerId} not found.");
        if (r.PartId is int partId && !await db.Parts.AnyAsync(p => p.Id == partId, ct))
            throw new KeyNotFoundException($"Part {partId} not found.");

        var tracking = Clean(r.TrackingNumber);
        var now = clock.UtcNow;
        var s = new SampleShipment
        {
            LeadId = r.LeadId,
            CustomerId = r.CustomerId,
            PartId = r.PartId,
            PartDescription = Clean(r.PartDescription),
            Quantity = Math.Max(1, r.Quantity),
            Status = tracking is null ? "Requested" : "Shipped",
            RequestedAt = now,
            ShippedAt = tracking is null ? null : now,
            Carrier = Clean(r.Carrier),
            TrackingNumber = tracking,
            ShipToAddress = Clean(r.ShipToAddress),
            FollowUpDate = r.FollowUpDate,
            Notes = Clean(r.Notes),
        };
        db.SampleShipments.Add(s);
        var what = $"{s.Quantity}× {s.PartDescription ?? "part"}";
        db.LogActivityAt(
            tracking is null ? "sample-requested" : "sample-shipped",
            tracking is null ? $"Sample shipment requested ({what})" : $"Sample shipped ({what}), tracking {tracking}",
            SampleShipmentProjection.IndexingPoints(s));
        await db.SaveChangesAsync(ct);
        return SampleShipmentProjection.ToModel(s);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
