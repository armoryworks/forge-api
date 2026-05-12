using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PurchaseOrders;

public record CreatePurchaseOrderReleaseCommand(int PurchaseOrderId, CreatePurchaseOrderReleaseRequestModel Request)
    : IRequest<PurchaseOrderReleaseResponseModel>;

public class CreatePurchaseOrderReleaseValidator : AbstractValidator<CreatePurchaseOrderReleaseCommand>
{
    public CreatePurchaseOrderReleaseValidator()
    {
        RuleFor(x => x.Request.PurchaseOrderLineId).GreaterThan(0);
        RuleFor(x => x.Request.Quantity).GreaterThan(0);
    }
}

public class CreatePurchaseOrderReleaseHandler(AppDbContext db) : IRequestHandler<CreatePurchaseOrderReleaseCommand, PurchaseOrderReleaseResponseModel>
{
    public async Task<PurchaseOrderReleaseResponseModel> Handle(CreatePurchaseOrderReleaseCommand request, CancellationToken cancellationToken)
    {
        var po = await db.PurchaseOrders
            .Include(p => p.Releases)
            .FirstOrDefaultAsync(p => p.Id == request.PurchaseOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.PurchaseOrderId} not found");

        if (!po.IsBlanket)
            throw new InvalidOperationException("Releases are only available for blanket purchase orders");

        var line = await db.PurchaseOrderLines
            .Include(l => l.Part)
            .FirstOrDefaultAsync(l => l.Id == request.Request.PurchaseOrderLineId && l.PurchaseOrderId == request.PurchaseOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"PO line {request.Request.PurchaseOrderLineId} not found on this PO");

        var nextRelease = po.Releases.Any() ? po.Releases.Max(r => r.ReleaseNumber) + 1 : 1;

        var release = new PurchaseOrderRelease
        {
            PurchaseOrderId = request.PurchaseOrderId,
            ReleaseNumber = nextRelease,
            PurchaseOrderLineId = request.Request.PurchaseOrderLineId,
            Quantity = request.Request.Quantity,
            RequestedDeliveryDate = request.Request.RequestedDeliveryDate,
            Notes = request.Request.Notes,
        };

        db.PurchaseOrderReleases.Add(release);

        // Update blanket released quantity
        po.BlanketReleasedQuantity = (po.BlanketReleasedQuantity ?? 0) + request.Request.Quantity;

        await db.SaveChangesAsync(cancellationToken);

        return new PurchaseOrderReleaseResponseModel
        {
            Id = release.Id,
            ReleaseNumber = release.ReleaseNumber,
            PurchaseOrderLineId = release.PurchaseOrderLineId,
            PartNumber = line.Part.PartNumber,
            PartDescription = line.Description,
            Quantity = release.Quantity,
            RequestedDeliveryDate = release.RequestedDeliveryDate,
            ActualDeliveryDate = release.ActualDeliveryDate,
            Status = release.Status,
            Notes = release.Notes,
            CreatedAt = release.CreatedAt,
        };
    }
}
