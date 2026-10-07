using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Inventory;

public record WaiveInspectionCommand(int ReceivingRecordId, string Reason) : IRequest;

public class WaiveInspectionValidator : AbstractValidator<WaiveInspectionCommand>
{
    public WaiveInspectionValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}

public class WaiveInspectionHandler(AppDbContext db, IHttpContextAccessor httpContext, IClock clock)
    : IRequestHandler<WaiveInspectionCommand>
{
    public async Task Handle(WaiveInspectionCommand request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var record = await db.ReceivingRecords
            .Include(r => r.PurchaseOrderLine)
            .FirstOrDefaultAsync(r => r.Id == request.ReceivingRecordId, ct)
            ?? throw new KeyNotFoundException($"ReceivingRecord {request.ReceivingRecordId} not found.");

        await ReceivingInspectionLock.LockAsync(db, record, ct);

        if (record.InspectionStatus is not (ReceivingInspectionStatus.Pending or ReceivingInspectionStatus.InProgress))
            throw new InvalidOperationException(
                $"Receiving record {record.Id} is {record.InspectionStatus}; only a pending or in-progress inspection can be waived.");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var reason = request.Reason.Trim();

        record.InspectionStatus = ReceivingInspectionStatus.Waived;
        record.InspectedById = userId;
        record.InspectedAt = clock.UtcNow;
        record.InspectionNotes = reason;

        db.LogActivityAt(
            "inspection-waived",
            $"Waived receiving inspection on {record.ReceiptNumber ?? $"receipt {record.Id}"}: {reason}",
            ("PurchaseOrder", record.PurchaseOrderLine.PurchaseOrderId));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
