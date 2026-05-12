using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
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
    }
}

public class SendOutSubcontractHandler(AppDbContext db)
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

        var order = new SubcontractOrder
        {
            JobId = request.JobId,
            OperationId = request.OperationId,
            VendorId = operation.SubcontractVendorId.Value,
            Quantity = request.Data.Quantity,
            UnitCost = request.Data.UnitCost,
            SentAt = DateTimeOffset.UtcNow,
            ExpectedReturnDate = request.Data.ExpectedReturnDate,
            ShippingTrackingNumber = request.Data.ShippingTrackingNumber?.Trim(),
            Notes = request.Data.Notes?.Trim(),
            Status = SubcontractStatus.Sent,
        };

        db.SubcontractOrders.Add(order);
        await db.SaveChangesAsync(ct);

        return new SubcontractOrderResponseModel(
            order.Id, order.JobId, job.JobNumber ?? $"J-{job.Id}",
            order.OperationId, operation.Title,
            order.VendorId, vendor.CompanyName,
            order.PurchaseOrderId, null,
            order.Quantity, order.UnitCost, order.Quantity * order.UnitCost,
            order.SentAt, order.ExpectedReturnDate, order.ReceivedAt,
            order.ReceivedQuantity, order.Status.ToString(),
            order.ShippingTrackingNumber, order.ReturnTrackingNumber, order.Notes,
            order.CreatedAt);
    }
}
