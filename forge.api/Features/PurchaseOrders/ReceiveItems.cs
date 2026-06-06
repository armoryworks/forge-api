using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.DomainEvents;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PurchaseOrders;

public record ReceiveItemsCommand(
    int PurchaseOrderId,
    List<ReceiveLineModel> Lines,
    decimal? ActualFreight = null,
    FreightAllocationMethod FreightAllocationMethod = FreightAllocationMethod.ByExtendedValue) : IRequest;

public class ReceiveItemsHandler(
    IPurchaseOrderRepository repo,
    IClock clock,
    IMediator mediator,
    IHttpContextAccessor httpContext,
    // Phase-2 STAGE C — optional / null-default so the handler stays constructible without an accounting
    // context (isolated unit tests). The production DI path supplies both; with CAP-ACCT-FULLGL off the
    // posting service no-ops. db is null only in those tests → no transaction is opened.
    AppDbContext? db = null,
    IReceiptInventoryPostingService? receiptPosting = null)
    : IRequestHandler<ReceiveItemsCommand>
{
    public async Task Handle(ReceiveItemsCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindWithDetailsAsync(request.PurchaseOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.PurchaseOrderId} not found");

        // F-033: source-state whitelist — Draft POs haven't been sent to vendor
        if (po.Status != PurchaseOrderStatus.Submitted &&
            po.Status != PurchaseOrderStatus.Acknowledged &&
            po.Status != PurchaseOrderStatus.PartiallyReceived)
            throw new InvalidOperationException(
                $"Cannot receive items on a purchase order in status {po.Status}. " +
                "Allowed: Submitted, Acknowledged, PartiallyReceived.");

        // Bought-parts effort PR3 — receipt-level freight capture. All
        // ReceivingRecords created in this call share a ReceiptNumber and
        // the same ActualFreight value (each row gets a per-line slice in
        // AllocatedFreight via the chosen allocation method). When the
        // caller didn't supply ActualFreight, default it from the PO's
        // EstimatedFreight; the buyer "matches estimate" with one click
        // (the variance check is a UI concern; we record the captured
        // value either way).
        var receiptNumber = $"R-{clock.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var actualFreight = request.ActualFreight ?? po.EstimatedFreight;

        // Pre-compute per-line allocation. ByExtendedValue / ByQuantity
        // need the totals, so we collect first then divvy up. Manual reads
        // each request line's ManualFreight directly. Weight allocation
        // falls back to ByExtendedValue when any part lacks a weight.
        var newRecords = new List<(ReceiveLineModel req, PurchaseOrderLine line, ReceivingRecord rec)>();
        decimal totalExtended = 0m;
        decimal totalQty = 0m;
        foreach (var receiveItem in request.Lines)
        {
            var line = po.Lines.FirstOrDefault(l => l.Id == receiveItem.LineId)
                ?? throw new KeyNotFoundException($"Line {receiveItem.LineId} not found on this purchase order");

            if (receiveItem.Quantity <= 0)
                throw new InvalidOperationException("Receive quantity must be positive");

            if (receiveItem.Quantity > line.RemainingQuantity)
                throw new InvalidOperationException($"Cannot receive {receiveItem.Quantity} — only {line.RemainingQuantity} remaining");

            line.ReceivedQuantity += receiveItem.Quantity;

            var rec = new ReceivingRecord
            {
                PurchaseOrderLineId = line.Id,
                QuantityReceived = receiveItem.Quantity,
                StorageLocationId = receiveItem.StorageLocationId,
                Notes = receiveItem.Notes,
                ReceiptNumber = receiptNumber,
                ActualFreight = actualFreight,
                FreightAllocationMethod = request.FreightAllocationMethod,
                // Filled in below once totals are known.
                AllocatedFreight = null,
            };
            newRecords.Add((receiveItem, line, rec));

            totalExtended += receiveItem.Quantity * line.UnitPrice;
            totalQty += receiveItem.Quantity;
        }

        // Allocation. Skip when no freight to allocate.
        if (actualFreight.HasValue && actualFreight.Value > 0m && newRecords.Count > 0)
        {
            switch (request.FreightAllocationMethod)
            {
                case FreightAllocationMethod.Manual:
                    foreach (var (req, _, rec) in newRecords)
                        rec.AllocatedFreight = req.ManualFreight ?? 0m;
                    break;

                case FreightAllocationMethod.ByQuantity:
                    if (totalQty > 0m)
                    {
                        foreach (var (_, _, rec) in newRecords)
                            rec.AllocatedFreight = Math.Round(actualFreight.Value * (rec.QuantityReceived / totalQty), 4);
                    }
                    break;

                case FreightAllocationMethod.ByWeight:
                    // Bought-parts effort PR4 — weighted split using
                    // Part.WeightEach (canonical SI = grams). Falls back
                    // to ByExtendedValue if any part on the receipt has
                    // no weight populated, since a partial weight set
                    // would silently mis-allocate. Both branches treat
                    // qty × weight per line consistently.
                    var totalWeight = newRecords.Sum(t => (t.line.Part?.WeightEach ?? 0m) * t.rec.QuantityReceived);
                    var allHaveWeight = newRecords.All(t => t.line.Part?.WeightEach is > 0);
                    if (allHaveWeight && totalWeight > 0m)
                    {
                        foreach (var (_, line, rec) in newRecords)
                        {
                            var lineWeight = (line.Part?.WeightEach ?? 0m) * rec.QuantityReceived;
                            rec.AllocatedFreight = Math.Round(actualFreight.Value * (lineWeight / totalWeight), 4);
                        }
                    }
                    else
                    {
                        // Mixed-weight receipt — quietly fall back. The buyer
                        // can set weights in the part identity cluster and
                        // re-receive, or pick Manual for this shipment.
                        goto case FreightAllocationMethod.ByExtendedValue;
                    }
                    break;

                case FreightAllocationMethod.ByExtendedValue:
                default:
                    if (totalExtended > 0m)
                    {
                        foreach (var (_, line, rec) in newRecords)
                        {
                            var extended = rec.QuantityReceived * line.UnitPrice;
                            rec.AllocatedFreight = Math.Round(actualFreight.Value * (extended / totalExtended), 4);
                        }
                    }
                    break;
            }
        }

        foreach (var (_, _, rec) in newRecords)
            await repo.AddReceivingRecordAsync(rec, cancellationToken);

        var allReceived = po.Lines.All(l => l.RemainingQuantity <= 0);
        var anyReceived = po.Lines.Any(l => l.ReceivedQuantity > 0);

        if (allReceived)
        {
            po.Status = PurchaseOrderStatus.Received;
            po.ReceivedDate = clock.UtcNow;
        }
        else if (anyReceived)
        {
            po.Status = PurchaseOrderStatus.PartiallyReceived;
        }

        // Resolve the actor once (tolerant) — used as the accounting PostedBy and on the domain event.
        var userId = int.TryParse(
            httpContext.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var uid) ? uid : 0;

        // One transaction: the receiving records + PO-status flip AND the inline inventory / GRNI posting
        // commit (or roll back) together — the locked inline model (§2). The engine's SaveChanges joins
        // this transaction; the handler commits once, so a posting failure unwinds the receipt too. On
        // Npgsql this is a real transaction; on the in-memory test provider it's an ignored no-op (and db
        // is null in the mock-based handler tests, so no transaction is opened there at all).
        await using var tx = db is not null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await repo.SaveChangesAsync(cancellationToken);

        // Inline inventory / GRNI posting (Phase-2 STAGE C). Runs AFTER the operational SaveChanges so the
        // receiving records are flushed and resolvable by ReceiptNumber; no-op while CAP-ACCT-FULLGL is off.
        if (receiptPosting is not null)
        {
            var entryDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            await receiptPosting.PostReceiptAsync(
                request.PurchaseOrderId, receiptNumber, entryDate, userId, cancellationToken);
        }

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);

        await mediator.Publish(new PurchaseOrderReceivedEvent(request.PurchaseOrderId, 0, userId), cancellationToken);
    }
}
