using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.PaymentTerms;
using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Extensions;
using Serilog;

namespace Forge.Api.Features.Inventory;

/// <inheritdoc />
public sealed class ConsignmentStockService(
    AppDbContext db,
    IInventoryRepository inventory,
    IVendorBillRepository bills,
    IPostingEngine postingEngine,
    IInventoryValuationService valuation,
    ICapabilitySnapshotProvider capabilities,
    IClock clock) : IConsignmentStockService
{
    internal const string LiabilityKey = "CONSIGNMENT_LIABILITY";
    internal const string SourceType = "ConsignmentTransaction";
    private const string FullGlCapability = "CAP-ACCT-FULLGL";
    private const string BillCapability = "CAP-P2P-BILL";

    public async Task<ConsignmentTransactionResponseModel> ReceiveAsync(
        int agreementId, RecordConsignmentTransactionRequestModel request, int userId, CancellationToken ct = default)
    {
        if (request.Quantity <= 0m)
            throw new InvalidOperationException("The quantity received must be greater than zero.");

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var agreement = await InboundAgreementAsync(agreementId, ct);
        if (agreement.Status != ConsignmentAgreementStatus.Active)
            throw new InvalidOperationException($"Agreement CA-{agreement.Id} is {agreement.Status}; only an active agreement receives stock.");

        var locationId = request.StorageLocationId ?? (await inventory.EnsureDefaultLocationAsync(ct)).Id;
        if (!await db.StorageLocations.AnyAsync(l => l.Id == locationId && l.IsActive, ct))
            throw new KeyNotFoundException($"Location {locationId} not found");

        var lot = string.IsNullOrWhiteSpace(request.LotNumber) ? null : request.LotNumber.Trim();
        var now = clock.UtcNow;
        var row = await db.BinContents.FirstOrDefaultAsync(c =>
            c.ConsignmentAgreementId == agreement.Id && c.LocationId == locationId && c.LotNumber == lot
            && c.RemovedAt == null && c.Status == BinContentStatus.Stored, ct);
        if (row is null)
        {
            row = new BinContent
            {
                LocationId = locationId,
                EntityType = "part",
                EntityId = agreement.PartId,
                Quantity = request.Quantity,
                LotNumber = lot,
                PlacedBy = userId,
                PlacedAt = now,
                Notes = $"Consigned by vendor under CA-{agreement.Id}",
                OwnerVendorId = agreement.VendorId,
                ConsignmentAgreementId = agreement.Id,
            };
            db.BinContents.Add(row);
        }
        else
        {
            row.Quantity += request.Quantity;
        }

        var movement = new BinMovement
        {
            EntityType = "part",
            EntityId = agreement.PartId,
            Quantity = request.Quantity,
            LotNumber = lot,
            ToLocationId = locationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.Receive,
            ConsignmentAgreementId = agreement.Id,
            Notes = $"Consignment receipt, vendor-owned (CA-{agreement.Id})",
        };
        db.BinMovements.Add(movement);
        await db.SaveChangesAsync(ct);

        var transaction = new ConsignmentTransaction
        {
            AgreementId = agreement.Id,
            Type = ConsignmentTransactionType.Receipt,
            Quantity = request.Quantity,
            UnitPrice = agreement.AgreedUnitPrice,
            ExtendedAmount = Money(request.Quantity * agreement.AgreedUnitPrice),
            PurchaseOrderId = request.PurchaseOrderId,
            BinContentId = row.Id,
            BinMovementId = movement.Id,
            StorageLocationId = locationId,
            CreatedByUserId = userId,
            Notes = request.Notes,
            CreatedAt = now,
        };
        db.ConsignmentTransactions.Add(transaction);
        db.LogActivityAt("consignment-received",
            $"Received {Qty(request.Quantity)} {agreement.Part.PartNumber} on consignment (vendor-owned)",
            ("ConsignmentAgreement", agreement.Id));
        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return (await GetTransactionsAsync(agreement.Id, ct)).First(t => t.Id == transaction.Id);
    }

    public async Task<(ConsignmentTransaction Transaction, IReadOnlyList<BinContent> OwnedRows)> TakeOwnershipAsync(
        int agreementId, decimal quantity, int? binContentId, int? storageLocationId, int? jobId, string? notes, int userId,
        CancellationToken ct = default)
    {
        if (quantity <= 0m)
            throw new InvalidOperationException("The quantity consumed must be greater than zero.");

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.AcquireAdvisoryLockAsync("inventory.consignment", agreementId.ToString(CultureInfo.InvariantCulture), ct);
        var agreement = await InboundAgreementAsync(agreementId, ct);

        var rows = await db.BinContents
            .Where(c => c.ConsignmentAgreementId == agreement.Id && c.RemovedAt == null && c.Quantity > 0m
                && (binContentId == null || c.Id == binContentId)
                && (storageLocationId == null || c.LocationId == storageLocationId))
            .OrderBy(c => c.PlacedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);
        if (binContentId is int requested && rows.Count == 0)
            throw new KeyNotFoundException($"Bin content {requested} holds no stock consigned under CA-{agreement.Id}");

        var free = rows.Where(r => r.Status != BinContentStatus.QcHold).Sum(r => r.Quantity - r.ReservedQuantity);
        if (free < quantity)
            throw new InvalidOperationException(
                $"Only {Qty(free)} of {agreement.Part.PartNumber} is on consignment there; {Qty(quantity)} was asked for.");

        var remaining = quantity;
        var owned = new List<BinContent>();
        ConsignmentTransaction? first = null;
        foreach (var row in rows.Where(r => r.Status != BinContentStatus.QcHold))
        {
            if (remaining <= 0m)
                break;
            var take = Math.Min(remaining, row.Quantity - row.ReservedQuantity);
            if (take <= 0m)
                continue;
            var (transaction, ownedRow) = await TransferAsync(agreement, row, take, jobId, notes, userId, ct);
            first ??= transaction;
            owned.Add(ownedRow);
            remaining -= take;
        }

        if (tx is not null)
            await tx.CommitAsync(ct);
        return (first!, owned);
    }

    public async Task<BinContent> TakeOwnershipOfRowAsync(
        BinContent consigned, decimal quantity, int? jobId, string? notes, int userId, CancellationToken ct = default)
    {
        if (consigned.ConsignmentAgreementId is not int agreementId)
            throw new InvalidOperationException("That bin holds company-owned stock.");
        if (consigned.Status == BinContentStatus.QcHold)
            throw new InvalidOperationException("That consigned stock is on quality hold.");
        if (consigned.Quantity - consigned.ReservedQuantity < quantity)
            throw new InvalidOperationException(
                $"Insufficient quantity in bin. Available: {Qty(consigned.Quantity - consigned.ReservedQuantity)}, Requested: {Qty(quantity)}");

        var agreement = await InboundAgreementAsync(agreementId, ct);
        var (_, owned) = await TransferAsync(agreement, consigned, quantity, jobId, notes, userId, ct);
        return owned;
    }

    public async Task<ConsignmentReconciliationResponseModel> ReconcileAsync(
        int agreementId, ReconcileConsignmentRequestModel request, int userId, CancellationToken ct = default)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.AcquireAdvisoryLockAsync("inventory.consignment", agreementId.ToString(CultureInfo.InvariantCulture), ct);

        var agreement = await db.ConsignmentAgreements
            .Include(a => a.Part).Include(a => a.Vendor)
            .FirstOrDefaultAsync(a => a.Id == agreementId, ct)
            ?? throw new KeyNotFoundException($"Consignment agreement {agreementId} not found");
        var now = clock.UtcNow;

        var rows = await db.BinContents
            .Where(c => c.ConsignmentAgreementId == agreement.Id && c.RemovedAt == null)
            .OrderBy(c => c.PlacedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);
        var bookQuantity = rows.Sum(r => r.Quantity);
        decimal variance = 0m;
        int? adjustmentId = null;

        if (request.PhysicalCount is decimal counted)
        {
            if (counted < 0m)
                throw new InvalidOperationException("A counted quantity cannot be negative.");
            variance = counted - bookQuantity;
            if (variance != 0m)
                adjustmentId = await AdjustConsignedAsync(agreement, rows, variance, bookQuantity, counted, userId, now, ct);
        }

        var from = request.PeriodFrom is DateOnly f ? new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : (DateTimeOffset?)null;
        var to = request.PeriodTo is DateOnly t ? new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : (DateTimeOffset?)null;
        var unbilled = await db.ConsignmentTransactions
            .Where(x => x.AgreementId == agreement.Id && x.Type == ConsignmentTransactionType.Consumption && x.VendorBillId == null
                && (from == null || x.CreatedAt >= from) && (to == null || x.CreatedAt < to))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .ToListAsync(ct);

        var billingAvailable = capabilities.IsEnabled(BillCapability) && agreement.VendorId is not null
            && agreement.Direction == ConsignmentDirection.Inbound;
        VendorBill? bill = null;
        if (request.CreateBill && billingAvailable && unbilled.Count > 0)
            bill = await DraftBillAsync(agreement, unbilled, request, userId, now, ct);

        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);

        var ids = unbilled.Select(u => u.Id).ToHashSet();
        var consumed = (await GetTransactionsAsync(agreement.Id, ct)).Where(m => ids.Contains(m.Id)).OrderBy(m => m.CreatedAt).ToList();
        return new ConsignmentReconciliationResponseModel
        {
            AgreementId = agreement.Id,
            BookQuantity = bookQuantity,
            PhysicalQuantity = request.PhysicalCount ?? bookQuantity,
            Variance = variance,
            AdjustmentTransactionId = adjustmentId,
            PeriodFrom = request.PeriodFrom,
            PeriodTo = request.PeriodTo,
            ConsumedTransactions = consumed,
            ConsumedQuantity = unbilled.Sum(u => u.Quantity),
            ConsumedAmount = unbilled.Sum(u => u.ExtendedAmount),
            VendorBillId = bill?.Id,
            VendorBillNumber = bill?.BillNumber,
            BillingAvailable = billingAvailable,
        };
    }

    public async Task<IReadOnlyList<ConsignmentTransactionResponseModel>> GetTransactionsAsync(int agreementId, CancellationToken ct = default)
    {
        var rows = await db.ConsignmentTransactions.AsNoTracking()
            .Where(t => t.AgreementId == agreementId)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Take(500)
            .Select(t => new
            {
                t,
                LocationName = db.StorageLocations.Where(l => l.Id == t.StorageLocationId).Select(l => l.Name).FirstOrDefault(),
                Lot = db.BinContents.Where(c => c.Id == t.BinContentId).Select(c => c.LotNumber).FirstOrDefault(),
                JobNumber = db.Jobs.Where(j => j.Id == t.JobId).Select(j => j.JobNumber).FirstOrDefault(),
                BillNumber = t.VendorBill != null ? t.VendorBill.BillNumber : null,
            })
            .ToListAsync(ct);

        return rows.Select(r => new ConsignmentTransactionResponseModel
        {
            Id = r.t.Id,
            AgreementId = r.t.AgreementId,
            Type = r.t.Type,
            Quantity = r.t.Quantity,
            UnitPrice = r.t.UnitPrice,
            ExtendedAmount = r.t.ExtendedAmount,
            PurchaseOrderId = r.t.PurchaseOrderId,
            InvoiceId = r.t.InvoiceId,
            BinContentId = r.t.BinContentId,
            StorageLocationId = r.t.StorageLocationId,
            StorageLocationName = r.LocationName,
            LotNumber = r.Lot,
            JobId = r.t.JobId,
            JobNumber = r.JobNumber,
            VendorBillId = r.t.VendorBillId,
            VendorBillNumber = r.BillNumber,
            JournalEntryId = r.t.JournalEntryId,
            Notes = r.t.Notes,
            CreatedAt = r.t.CreatedAt,
        }).ToList();
    }

    private async Task<(ConsignmentTransaction Transaction, BinContent Owned)> TransferAsync(
        ConsignmentAgreement agreement, BinContent consigned, decimal quantity, int? jobId, string? notes, int userId,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        consigned.Quantity -= quantity;
        if (consigned.Quantity == 0m && consigned.ReservedQuantity == 0m)
        {
            consigned.RemovedAt = now;
            consigned.RemovedBy = userId;
        }

        var owned = await ScanBinStock.AddAsync(
            db, agreement.PartId, consigned.LocationId, consigned.LotNumber, quantity, userId, now, ct);
        var movement = new BinMovement
        {
            EntityType = "part",
            EntityId = agreement.PartId,
            Quantity = quantity,
            LotNumber = consigned.LotNumber,
            FromLocationId = consigned.LocationId,
            ToLocationId = consigned.LocationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.ConsignmentConsume,
            ConsignmentAgreementId = agreement.Id,
            Notes = $"Consigned stock taken into ownership (CA-{agreement.Id})",
        };
        db.BinMovements.Add(movement);
        await db.SaveChangesAsync(ct);

        var transaction = new ConsignmentTransaction
        {
            AgreementId = agreement.Id,
            Type = ConsignmentTransactionType.Consumption,
            Quantity = quantity,
            UnitPrice = agreement.AgreedUnitPrice,
            ExtendedAmount = Money(quantity * agreement.AgreedUnitPrice),
            BinContentId = consigned.Id,
            BinMovementId = movement.Id,
            StorageLocationId = consigned.LocationId,
            JobId = jobId,
            CreatedByUserId = userId,
            Notes = notes,
            CreatedAt = now,
        };
        db.ConsignmentTransactions.Add(transaction);
        await db.SaveChangesAsync(ct);

        transaction.JournalEntryId = await PostConsumptionAsync(agreement, transaction, userId, now, ct);
        db.LogActivityAt("consignment-consumed",
            $"Consumed {Qty(quantity)} {agreement.Part.PartNumber} from consignment at {agreement.AgreedUnitPrice:0.####} each",
            ("ConsignmentAgreement", agreement.Id));
        await db.SaveChangesAsync(ct);
        return (transaction, owned);
    }

    private async Task<long?> PostConsumptionAsync(
        ConsignmentAgreement agreement, ConsignmentTransaction transaction, int userId, DateTimeOffset now, CancellationToken ct)
    {
        if (!capabilities.IsEnabled(FullGlCapability) || transaction.ExtendedAmount <= 0m)
            return null;

        var part = agreement.Part;
        if (part.InventoryClass is not (InventoryClass.Raw or InventoryClass.Component or InventoryClass.Subassembly or InventoryClass.FinishedGood))
        {
            Log.Warning("Consignment consumption {TransactionId} not posted: part {PartNumber} is not carried in inventory.",
                transaction.Id, part.PartNumber);
            return null;
        }

        var book = await db.Books.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct)
            ?? throw new PostingException("NO_POSTING_BOOK",
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post the consignment consumption into.");
        if (!await db.AccountDeterminationRules.AnyAsync(r => r.BookId == book.Id && r.Key == LiabilityKey, ct))
            throw new InvalidOperationException(
                $"The book has no account mapped to {LiabilityKey}; map the consignment liability account before consuming consigned stock.");

        var idempotencyKey = $"{JournalSource.Inventory}:{SourceType}:{transaction.Id}:CONSUME";
        var existing = await db.JournalEntries.IgnoreQueryFilters()
            .Where(e => e.BookId == book.Id && e.IdempotencyKey == idempotencyKey)
            .Select(e => (long?)e.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return existing;

        await valuation.ApplyReceiptAsync(book.Id, part.Id, transaction.Quantity, transaction.ExtendedAmount, ct);
        var description = $"Consigned {part.PartNumber} x{Qty(transaction.Quantity)} taken into ownership (CA-{agreement.Id})";
        var entry = await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = DateOnly.FromDateTime(now.UtcDateTime),
            Source = JournalSource.Inventory,
            SourceType = SourceType,
            SourceId = transaction.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = description,
            IdempotencyKey = idempotencyKey,
            Lines =
            [
                new PostingLine { AccountKey = InventoryValuationService.StoreKeyFor(part.InventoryClass), Debit = transaction.ExtendedAmount, Description = description },
                new PostingLine { AccountKey = LiabilityKey, Credit = transaction.ExtendedAmount, Description = description },
            ],
        }, userId, ct);
        return entry.Id;
    }

    private async Task<int> AdjustConsignedAsync(
        ConsignmentAgreement agreement, List<BinContent> rows, decimal variance, decimal book, decimal counted, int userId,
        DateTimeOffset now, CancellationToken ct)
    {
        int? locationId;
        if (variance < 0m)
        {
            var free = rows.Sum(r => r.Quantity - r.ReservedQuantity);
            if (free < -variance)
                throw new InvalidOperationException("The count is below the consigned quantity that is reserved; release the reservation first.");
            var drawn = BinContentDrawDown.Take(rows, -variance, userId, now);
            foreach (var (row, taken) in drawn)
                db.BinMovements.Add(AdjustmentMovement(agreement, row.LocationId, row.LotNumber, -taken, userId, now));
            locationId = drawn.FirstOrDefault().Row?.LocationId;
        }
        else
        {
            var row = rows.FirstOrDefault(r => r.Status == BinContentStatus.Stored);
            if (row is null)
            {
                var location = (await inventory.EnsureDefaultLocationAsync(ct)).Id;
                row = new BinContent
                {
                    LocationId = location,
                    EntityType = "part",
                    EntityId = agreement.PartId,
                    Quantity = 0m,
                    PlacedBy = userId,
                    PlacedAt = now,
                    Notes = $"Consigned by vendor under CA-{agreement.Id}",
                    OwnerVendorId = agreement.VendorId,
                    ConsignmentAgreementId = agreement.Id,
                };
                db.BinContents.Add(row);
            }
            row.Quantity += variance;
            db.BinMovements.Add(AdjustmentMovement(agreement, row.LocationId, row.LotNumber, variance, userId, now));
            locationId = row.LocationId;
        }

        var adjustment = new ConsignmentTransaction
        {
            AgreementId = agreement.Id,
            Type = ConsignmentTransactionType.Reconciliation,
            Quantity = variance,
            UnitPrice = agreement.AgreedUnitPrice,
            ExtendedAmount = Money(variance * agreement.AgreedUnitPrice),
            StorageLocationId = locationId,
            CreatedByUserId = userId,
            Notes = $"Reconciliation adjustment: book={Qty(book)}, physical={Qty(counted)}",
            CreatedAt = now,
        };
        db.ConsignmentTransactions.Add(adjustment);
        db.LogActivityAt("consignment-counted",
            $"Consigned stock counted at {Qty(counted)} against {Qty(book)} on the books",
            ("ConsignmentAgreement", agreement.Id));
        await db.SaveChangesAsync(ct);
        return adjustment.Id;
    }

    private static BinMovement AdjustmentMovement(
        ConsignmentAgreement agreement, int locationId, string? lot, decimal delta, int userId, DateTimeOffset now) => new()
    {
        EntityType = "part",
        EntityId = agreement.PartId,
        Quantity = Math.Abs(delta),
        LotNumber = lot,
        FromLocationId = delta < 0m ? locationId : null,
        ToLocationId = delta > 0m ? locationId : null,
        MovedBy = userId,
        MovedAt = now,
        Reason = BinMovementReason.Adjustment,
        ConsignmentAgreementId = agreement.Id,
        Notes = $"Consignment count adjustment, vendor-owned (CA-{agreement.Id})",
    };

    private async Task<VendorBill> DraftBillAsync(
        ConsignmentAgreement agreement, List<ConsignmentTransaction> unbilled, ReconcileConsignmentRequestModel request,
        int userId, DateTimeOffset now, CancellationToken ct)
    {
        var terms = await new PaymentTermsResolver(db).FindAsync(agreement.Vendor?.PaymentTermsId, ct);
        var currencyId = await db.Books.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Id)
            .Select(b => b.FunctionalCurrencyId)
            .FirstOrDefaultAsync(ct) is int id and > 0 ? id : 1;
        var period = request.PeriodFrom is null && request.PeriodTo is null
            ? string.Empty
            : $" {request.PeriodFrom?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? "…"}–{request.PeriodTo?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? "…"}";

        var bill = new VendorBill
        {
            BillNumber = await bills.GenerateNextBillNumberAsync(ct),
            VendorId = agreement.VendorId!.Value,
            VendorInvoiceNumber = string.IsNullOrWhiteSpace(request.VendorInvoiceNumber) ? null : request.VendorInvoiceNumber.Trim(),
            CurrencyId = currencyId,
            FxRate = 1m,
            Status = VendorBillStatus.Draft,
            BillDate = now,
            DueDate = now.AddDays(terms?.NetDays ?? 0),
            CreatedByUserId = userId,
            Notes = $"Consignment consumption under CA-{agreement.Id}{period}",
        };
        PaymentTermsResolver.Stamp(bill, terms);
        var number = 0;
        foreach (var consumption in unbilled)
        {
            bill.Lines.Add(new VendorBillLine
            {
                LineNumber = ++number,
                PartId = agreement.PartId,
                JobId = consumption.JobId,
                Description = $"Consigned {agreement.Part.PartNumber} used {consumption.CreatedAt:MM/dd/yyyy}",
                Quantity = consumption.Quantity,
                UnitPrice = consumption.UnitPrice,
                AccountDeterminationKey = LiabilityKey,
            });
        }
        db.VendorBills.Add(bill);
        await db.SaveChangesAsync(ct);

        foreach (var consumption in unbilled)
            consumption.VendorBillId = bill.Id;
        db.LogActivityAt("created",
            $"Draft bill {bill.BillNumber} for consigned {agreement.Part.PartNumber} used: {Qty(unbilled.Sum(u => u.Quantity))}",
            ("VendorBill", bill.Id), ("ConsignmentAgreement", agreement.Id));
        return bill;
    }

    private async Task<ConsignmentAgreement> InboundAgreementAsync(int agreementId, CancellationToken ct)
    {
        var agreement = await db.ConsignmentAgreements.Include(a => a.Part)
            .FirstOrDefaultAsync(a => a.Id == agreementId, ct)
            ?? throw new KeyNotFoundException($"Consignment agreement {agreementId} not found");
        if (agreement.Direction != ConsignmentDirection.Inbound || agreement.VendorId is null)
            throw new InvalidOperationException(
                $"Agreement CA-{agreement.Id} is not an inbound vendor consignment; vendor-owned stock moves only under one.");
        return agreement;
    }

    private static decimal Money(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string Qty(decimal quantity) => quantity.ToString("0.####", CultureInfo.InvariantCulture);
}
