using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record ReceiveBackSubcontractCommand(int SubcontractOrderId, ReceiveBackRequestModel Data) : IRequest<SubcontractOrderResponseModel>;

public class ReceiveBackSubcontractValidator : AbstractValidator<ReceiveBackSubcontractCommand>
{
    public ReceiveBackSubcontractValidator()
    {
        RuleFor(x => x.Data.ReceivedQuantity).GreaterThan(0);
    }
}

public class ReceiveBackSubcontractHandler(AppDbContext db)
    : IRequestHandler<ReceiveBackSubcontractCommand, SubcontractOrderResponseModel>
{
    public async Task<SubcontractOrderResponseModel> Handle(ReceiveBackSubcontractCommand request, CancellationToken ct)
    {
        var order = await db.SubcontractOrders
            .Include(o => o.Job)
            .Include(o => o.Operation)
            .Include(o => o.Vendor)
            .FirstOrDefaultAsync(o => o.Id == request.SubcontractOrderId, ct)
            ?? throw new KeyNotFoundException($"SubcontractOrder {request.SubcontractOrderId} not found.");

        order.ReceivedAt = DateTimeOffset.UtcNow;
        order.ReceivedQuantity = request.Data.ReceivedQuantity;
        order.ReturnTrackingNumber = request.Data.ReturnTrackingNumber?.Trim();
        order.Notes = request.Data.Notes?.Trim() ?? order.Notes;
        order.Status = request.Data.PassedInspection
            ? SubcontractStatus.Complete
            : SubcontractStatus.Rejected;

        await db.SaveChangesAsync(ct);

        var poNumber = order.PurchaseOrderId.HasValue
            ? await db.PurchaseOrders.Where(p => p.Id == order.PurchaseOrderId).Select(p => p.PONumber).FirstOrDefaultAsync(ct)
            : null;

        return new SubcontractOrderResponseModel(
            order.Id, order.JobId, order.Job.JobNumber ?? $"J-{order.Job.Id}",
            order.OperationId, order.Operation.Title,
            order.VendorId, order.Vendor.CompanyName,
            order.PurchaseOrderId, poNumber,
            order.Quantity, order.UnitCost, order.Quantity * order.UnitCost,
            order.SentAt, order.ExpectedReturnDate, order.ReceivedAt,
            order.ReceivedQuantity, order.Status.ToString(),
            order.ShippingTrackingNumber, order.ReturnTrackingNumber, order.Notes,
            order.CreatedAt);
    }
}
