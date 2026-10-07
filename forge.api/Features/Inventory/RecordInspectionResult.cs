using System.Security.Claims;

using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Inventory;

public record RecordInspectionResultCommand(int ReceivingRecordId, InspectionResultRequestModel Data) : IRequest;

public class RecordInspectionResultValidator : AbstractValidator<RecordInspectionResultCommand>
{
    public RecordInspectionResultValidator()
    {
        RuleFor(x => x.Data.Result)
            .NotEmpty()
            .Must(r => r is not null && RecordInspectionResultHandler.AllowedResults.ContainsKey(r))
            .WithMessage("Result must be Passed, Failed or PartialAccept.");
        RuleFor(x => x.Data.AcceptedQuantity).NotNull().GreaterThanOrEqualTo(0);
        RuleFor(x => x.Data.RejectedQuantity).NotNull().GreaterThanOrEqualTo(0);
        RuleFor(x => x.Data.Notes).MaximumLength(2000);

        When(x => x.Data.Result == nameof(ReceivingInspectionStatus.PartialAccept), () =>
        {
            RuleFor(x => x.Data.AcceptedQuantity).GreaterThan(0)
                .WithMessage("A partial accept needs an accepted quantity above zero.");
            RuleFor(x => x.Data.RejectedQuantity).GreaterThan(0)
                .WithMessage("A partial accept needs a rejected quantity above zero.");
        });

        When(x => x.Data.Result == nameof(ReceivingInspectionStatus.Passed), () =>
        {
            RuleFor(x => x.Data.RejectedQuantity).Equal(0m)
                .WithMessage("A passed inspection cannot reject any quantity; record a partial accept instead.");
        });

        When(x => x.Data.Result == nameof(ReceivingInspectionStatus.Failed), () =>
        {
            RuleFor(x => x.Data.AcceptedQuantity).Equal(0m)
                .WithMessage("A failed inspection cannot accept any quantity; record a partial accept instead.");
        });
    }
}

public class RecordInspectionResultHandler(
    AppDbContext db,
    IHttpContextAccessor httpContext,
    INcrCapaService ncrCapaService,
    IClock clock)
    : IRequestHandler<RecordInspectionResultCommand>
{
    internal static readonly IReadOnlyDictionary<string, (ReceivingInspectionStatus Status, ReceivingInspectionResult Result)> AllowedResults =
        new Dictionary<string, (ReceivingInspectionStatus, ReceivingInspectionResult)>
        {
            [nameof(ReceivingInspectionStatus.Passed)] = (ReceivingInspectionStatus.Passed, ReceivingInspectionResult.Accept),
            [nameof(ReceivingInspectionStatus.Failed)] = (ReceivingInspectionStatus.Failed, ReceivingInspectionResult.Reject),
            [nameof(ReceivingInspectionStatus.PartialAccept)] = (ReceivingInspectionStatus.PartialAccept, ReceivingInspectionResult.ConditionalAccept),
        };

    public async Task Handle(RecordInspectionResultCommand request, CancellationToken ct)
    {
        var data = request.Data;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var record = await db.ReceivingRecords
            .Include(r => r.PurchaseOrderLine)
                .ThenInclude(l => l.PurchaseOrder)
            .FirstOrDefaultAsync(r => r.Id == request.ReceivingRecordId, ct)
            ?? throw new KeyNotFoundException($"ReceivingRecord {request.ReceivingRecordId} not found.");

        await ReceivingInspectionLock.LockAsync(db, record, ct);

        if (record.InspectionStatus is not (ReceivingInspectionStatus.Pending or ReceivingInspectionStatus.InProgress))
            throw new InvalidOperationException(
                $"Receiving record {record.Id} is {record.InspectionStatus}; only a pending or in-progress inspection can be recorded.");

        if (data.Result is null || !AllowedResults.TryGetValue(data.Result, out var outcome))
            throw new ValidationException([new ValidationFailure("Data.Result", "Result must be Passed, Failed or PartialAccept.")]);

        var accepted = data.AcceptedQuantity ?? 0m;
        var rejected = data.RejectedQuantity ?? 0m;
        if (accepted + rejected != record.QuantityReceived)
            throw new ValidationException([new ValidationFailure("Data.AcceptedQuantity",
                $"Accepted ({accepted}) plus rejected ({rejected}) must equal the received quantity ({record.QuantityReceived}).")]);

        var line = record.PurchaseOrderLine;
        var raiseNcr = data.CreateNcrOnReject && rejected > 0;
        if (raiseNcr && line.PartId is null)
            throw new InvalidOperationException(
                $"Receiving record {record.Id} is not for a part, so no NCR can be raised; clear the create-NCR option to record the rejection.");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = clock.UtcNow;
        var notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim();
        var receiptRef = record.ReceiptNumber ?? $"receipt {record.Id}";

        NonConformance? ncr = null;
        if (raiseNcr)
        {
            ncr = new NonConformance
            {
                NcrNumber = await ncrCapaService.GenerateNcrNumberAsync(ct),
                Type = NcrType.Supplier,
                PartId = line.PartId!.Value,
                PurchaseOrderLineId = line.Id,
                VendorId = line.PurchaseOrder.VendorId,
                QcInspectionId = data.QcInspectionId,
                DetectedById = userId,
                DetectedAt = now,
                DetectedAtStage = NcrDetectionStage.Receiving,
                Description = notes is null
                    ? $"Rejected {rejected} of {record.QuantityReceived} at receiving inspection of {receiptRef} (PO {line.PurchaseOrder.PONumber})."
                    : $"Rejected {rejected} of {record.QuantityReceived} at receiving inspection of {receiptRef} (PO {line.PurchaseOrder.PONumber}): {notes}",
                AffectedQuantity = rejected,
                DefectiveQuantity = rejected,
                Status = NcrStatus.Open,
            };
            db.NonConformances.Add(ncr);
            await db.SaveChangesAsync(ct);

            db.LogActivityAt(
                "created",
                $"Raised from receiving inspection of {receiptRef}, PO {line.PurchaseOrder.PONumber}: {rejected} rejected",
                ("NonConformance", ncr.Id));
        }

        record.InspectionStatus = outcome.Status;
        record.InspectedById = userId;
        record.InspectedAt = now;
        record.InspectionNotes = notes;
        record.InspectedQuantityAccepted = accepted;
        record.InspectedQuantityRejected = rejected;
        record.QcInspectionId = data.QcInspectionId;

        db.ReceivingInspections.Add(new ReceivingInspection
        {
            ReceivingRecordId = record.Id,
            QcInspectionId = data.QcInspectionId,
            Result = outcome.Result,
            AcceptedQuantity = accepted,
            RejectedQuantity = rejected,
            Notes = notes,
            InspectedById = userId,
            InspectedAt = now,
            NcrId = ncr?.Id,
        });

        db.LogActivityAt(
            "inspection-recorded",
            ncr is null
                ? $"Receiving inspection {outcome.Status} on {receiptRef}: {accepted} accepted, {rejected} rejected"
                : $"Receiving inspection {outcome.Status} on {receiptRef}: {accepted} accepted, {rejected} rejected, {ncr.NcrNumber} raised",
            ("PurchaseOrder", line.PurchaseOrderId));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
