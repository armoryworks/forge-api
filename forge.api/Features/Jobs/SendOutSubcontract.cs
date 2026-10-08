using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record SendOutSubcontractCommand(int JobId, int OperationId, SendOutRequestModel Data) : IRequest<SubcontractOrderResponseModel>;

public class SendOutSubcontractValidator : AbstractValidator<SendOutSubcontractCommand>
{
    public SendOutSubcontractValidator()
    {
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.UnitCost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Data.ShippingTrackingNumber).MaximumLength(200);
        RuleFor(x => x.Data.Notes).MaximumLength(2000);
    }
}

public class SendOutSubcontractHandler(AppDbContext db, IMediator mediator, IClock clock)
    : IRequestHandler<SendOutSubcontractCommand, SubcontractOrderResponseModel>
{
    public async Task<SubcontractOrderResponseModel> Handle(SendOutSubcontractCommand request, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == request.JobId, ct)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found.");

        var operation = await db.Operations
            .Include(o => o.SubcontractVendor)
            .FirstOrDefaultAsync(o => o.Id == request.OperationId && o.PartId == job.PartId, ct)
            ?? throw new KeyNotFoundException($"Operation {request.OperationId} not found for this job.");

        if (!operation.IsSubcontract || operation.SubcontractVendorId == null)
            throw new InvalidOperationException("Operation is not configured as a subcontract operation.");

        var vendor = operation.SubcontractVendor
            ?? await db.Vendors.AsNoTracking().FirstAsync(v => v.Id == operation.SubcontractVendorId, ct);

        var jobNumber = string.IsNullOrEmpty(job.JobNumber) ? $"J-{job.Id}" : job.JobNumber;
        var unitCost = request.Data.UnitCost > 0 ? request.Data.UnitCost : operation.SubcontractCost ?? 0m;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        PurchaseOrderListItemModel? po = null;
        if (request.Data.CreatePurchaseOrder)
        {
            po = await mediator.Send(new CreatePurchaseOrderCommand(
                vendor.Id,
                job.Id,
                null,
                [new CreatePurchaseOrderLineModel(
                    null,
                    $"Op {operation.StepNumber} {operation.Title} for {jobNumber}",
                    request.Data.Quantity,
                    unitCost,
                    null)],
                ExpectedDeliveryDate: request.Data.ExpectedReturnDate), ct);
        }

        var order = new SubcontractOrder
        {
            JobId = request.JobId,
            OperationId = request.OperationId,
            VendorId = vendor.Id,
            PurchaseOrderId = po?.Id,
            Quantity = request.Data.Quantity,
            UnitCost = unitCost,
            SentAt = clock.UtcNow,
            ExpectedReturnDate = request.Data.ExpectedReturnDate,
            ShippingTrackingNumber = request.Data.ShippingTrackingNumber?.Trim(),
            Notes = request.Data.Notes?.Trim(),
            Status = SubcontractStatus.Sent,
        };

        db.SubcontractOrders.Add(order);
        db.JobActivityLogs.Add(new JobActivityLog
        {
            JobId = job.Id,
            UserId = db.CurrentUserId,
            Action = ActivityAction.StatusChanged,
            FieldName = "Subcontract",
            NewValue = SubcontractStatus.Sent.ToString(),
            OperationId = operation.Id,
            Description = po is null
                ? $"Sent {request.Data.Quantity:0.####} out to {vendor.CompanyName} for Op {operation.StepNumber} {operation.Title}"
                : $"Sent {request.Data.Quantity:0.####} out to {vendor.CompanyName} for Op {operation.StepNumber} {operation.Title} on draft {po.PONumber}",
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new SubcontractOrderResponseModel(
            order.Id, order.JobId, jobNumber,
            order.OperationId, operation.Title,
            order.VendorId, vendor.CompanyName,
            order.PurchaseOrderId, po?.PONumber,
            order.Quantity, order.UnitCost, order.Quantity * order.UnitCost,
            order.SentAt, order.ExpectedReturnDate, order.ReceivedAt,
            order.ReceivedQuantity, order.Status.ToString(),
            order.ShippingTrackingNumber, order.ReturnTrackingNumber, order.Notes,
            order.CreatedAt);
    }
}
