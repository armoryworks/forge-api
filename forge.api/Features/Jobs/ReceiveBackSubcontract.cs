using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record ReceiveBackSubcontractCommand(int SubcontractOrderId, ReceiveBackRequestModel Data) : IRequest<SubcontractOrderResponseModel>;

public class ReceiveBackSubcontractValidator : AbstractValidator<ReceiveBackSubcontractCommand>
{
    public ReceiveBackSubcontractValidator()
    {
        RuleFor(x => x.Data.ReceivedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Data.ScrapQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Data.ReceivedQuantity + x.Data.ScrapQuantity)
            .GreaterThan(0)
            .WithName("ReceivedQuantity")
            .WithMessage("Enter a good or scrap quantity.");
        RuleFor(x => x.Data.ReturnTrackingNumber).MaximumLength(200);
        RuleFor(x => x.Data.Notes).MaximumLength(2000);
    }
}

public class ReceiveBackSubcontractHandler(AppDbContext db, IClock clock)
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

        if (order.ReceivedAt.HasValue || order.Status is SubcontractStatus.Complete or SubcontractStatus.Rejected)
            throw new InvalidOperationException("This subcontract order has already been received back.");

        var previousStatus = order.Status;
        order.ReceivedAt = clock.UtcNow;
        order.ReceivedById = db.CurrentUserId;
        order.ReceivedQuantity = request.Data.ReceivedQuantity;
        order.ReturnTrackingNumber = request.Data.ReturnTrackingNumber?.Trim();
        order.Notes = request.Data.Notes?.Trim() ?? order.Notes;
        order.Status = request.Data.PassedInspection
            ? SubcontractStatus.Complete
            : SubcontractStatus.Rejected;

        db.JobActivityLogs.Add(new JobActivityLog
        {
            JobId = order.JobId,
            UserId = db.CurrentUserId,
            Action = ActivityAction.StatusChanged,
            FieldName = "Subcontract",
            OldValue = previousStatus.ToString(),
            NewValue = order.Status.ToString(),
            OperationId = order.OperationId,
            Description = $"Received back {request.Data.ReceivedQuantity:0.####} good, {request.Data.ScrapQuantity:0.####} scrap from {order.Vendor.CompanyName} for Op {order.Operation.StepNumber} {order.Operation.Title}",
        });

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
