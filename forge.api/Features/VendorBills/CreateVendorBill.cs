using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PaymentTerms;
using Forge.Api.Features.Freight;
using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Services;
using Forge.Api.Services.Fx;
using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorBills;

/// <summary>
/// Creates a Draft <see cref="VendorBill"/> (AP counterpart of CreateInvoice). Creating a Draft is
/// <b>not</b> a posting trigger — the AP / expense journal posts on approval (see ApproveVendorBill).
/// </summary>
public record CreateVendorBillCommand(
    int VendorId,
    string? VendorInvoiceNumber,
    int? PurchaseOrderId,
    DateTimeOffset BillDate,
    DateTimeOffset DueDate,
    decimal TaxAmount,
    string? Notes,
    List<CreateVendorBillLineModel> Lines,
    // Multi-currency (Phase-4 FULLGL, additive) — mirrors CreateInvoiceCommand. Null CurrencyId → the active
    // book's functional currency; FxRate is the booking rate. Defaults keep single-currency callers unchanged.
    int? CurrencyId = null,
    decimal? FxRate = null,
    int? PaymentTermsId = null) : IRequest<VendorBillListItemModel>;

public class CreateVendorBillValidator : AbstractValidator<CreateVendorBillCommand>
{
    public CreateVendorBillValidator()
    {
        RuleFor(x => x.VendorId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleFor(x => x.TaxAmount).GreaterThanOrEqualTo(0).MoneyScale();
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.BillDate)
            .WithMessage("Due date must be on or after the bill date.");
        // FX booking rate must be positive (a 0 or negative rate would zero/invert the functional amount).
        RuleFor(x => x.FxRate).GreaterThan(0m).When(x => x.FxRate.HasValue);
        // A zero-total bill would flip to Approved yet post no journal (the engine skips non-positive
        // totals) — a silent status/ledger divergence. Require a positive total.
        RuleFor(x => x)
            .Must(c => Money.Round(c.Lines.Sum(l => l.Quantity * l.UnitPrice) + c.TaxAmount) > 0m)
            .WithMessage("Bill total must be greater than zero")
            .When(x => x.Lines is { Count: > 0 });
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.LineType).IsInEnum();
            line.RuleFor(l => l.Description).NotEmpty().When(l => l.LineType != DocumentLineType.Freight);
            line.RuleFor(l => l.PartId).Null()
                .When(l => l.LineType == DocumentLineType.Freight)
                .WithMessage("A freight line can't name a part.")
                .WithErrorCode(FreightLines.PartCode);
            line.RuleFor(l => l.PurchaseOrderLineId).Null()
                .When(l => l.LineType == DocumentLineType.Freight)
                .WithMessage("A freight line can't name a purchase-order line.")
                .WithErrorCode(FreightLines.PoLineCode);
            line.RuleFor(l => l.FreightForPurchaseOrderId).Null()
                .When(l => l.LineType != DocumentLineType.Freight)
                .WithMessage("Only a freight line can clear a purchase order's freight.");
            line.RuleFor(l => l.Quantity).GreaterThan(0m).UnitScale();
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0).UnitScale();
        });
        // 3-way match (STAGE D): a PO-linked bill must match each line to a PO line so the posting can
        // clear GRNI at the PO price; a standalone (non-PO) bill must NOT carry PO-line links.
        When(x => x.PurchaseOrderId is not null, () =>
            RuleForEach(x => x.Lines).ChildRules(line =>
                line.RuleFor(l => l.PurchaseOrderLineId)
                    .NotNull()
                    .When(l => l.LineType != DocumentLineType.Freight)
                    .WithMessage("Each line on a PO-linked bill must reference a purchase-order line (3-way match).")));
        When(x => x.PurchaseOrderId is null, () =>
            RuleForEach(x => x.Lines).ChildRules(line =>
                line.RuleFor(l => l.PurchaseOrderLineId)
                    .Null()
                    .WithMessage("A standalone (non-PO) bill line cannot reference a purchase-order line.")));
        When(x => x.PurchaseOrderId is not null, () =>
            RuleForEach(x => x.Lines).ChildRules(line =>
                line.RuleFor(l => l.GlAccountId)
                    .Null()
                    .WithMessage("A PO-linked bill line clears goods received not invoiced and cannot name a GL account.")));
        RuleForEach(x => x.Lines).ChildRules(line =>
            line.RuleFor(l => l.GlAccountId)
                .Null()
                .When(l => l.LineType == DocumentLineType.Freight && l.FreightForPurchaseOrderId is not null)
                .WithMessage("Freight for a purchase order clears its freight accrual and cannot name a GL account."));
    }
}

public class CreateVendorBillHandler(
    IVendorBillRepository repo,
    IVendorRepository vendorRepo,
    AppDbContext db)
    : IRequestHandler<CreateVendorBillCommand, VendorBillListItemModel>
{
    private const string GrniKey = "GRNI";
    private const string FreightInKey = "FREIGHT_IN";

    public async Task<VendorBillListItemModel> Handle(CreateVendorBillCommand request, CancellationToken cancellationToken)
    {
        var vendor = await vendorRepo.FindAsync(request.VendorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor {request.VendorId} not found");

        // Duplicate-vendor-invoice guard (pre-go-live AP control): never record the same vendor invoice twice
        // — the highest-value double-payment protection (we receive bills, unlike AR where we issue invoices).
        // A friendly 4xx here; the partial unique index ux_vendor_bills_vendor_invoice is the backstop.
        if (!string.IsNullOrWhiteSpace(request.VendorInvoiceNumber)
            && await repo.ExistsForVendorInvoiceAsync(request.VendorId, request.VendorInvoiceNumber, cancellationToken))
        {
            throw new InvalidOperationException(
                $"A bill for vendor {request.VendorId} with invoice number '{request.VendorInvoiceNumber}' already exists.");
        }

        await BillableGlAccounts.EnsureBillableAsync(
            db, request.Lines.Where(l => l.GlAccountId is not null).Select(l => l.GlAccountId!.Value), cancellationToken);

        var freightPoIds = request.Lines
            .Where(l => l.LineType == DocumentLineType.Freight && l.FreightForPurchaseOrderId is not null)
            .Select(l => l.FreightForPurchaseOrderId!.Value)
            .Distinct()
            .ToList();
        if (freightPoIds.Count > 0)
        {
            var found = await db.PurchaseOrders.AsNoTracking().CountAsync(p => freightPoIds.Contains(p.Id), cancellationToken);
            if (found != freightPoIds.Count)
                throw FreightLines.Failure("Lines", FreightLines.PurchaseOrderNotFoundCode,
                    "A freight line names a purchase order that does not exist.");
        }

        var billNumber = await repo.GenerateNextBillNumberAsync(cancellationToken);

        var currencies = new DocumentCurrencyResolver(db);
        int? inheritedCurrencyId = vendor.CurrencyId;
        var inheritedTermsId = vendor.PaymentTermsId;
        if (request.PurchaseOrderId is int linkedPoId)
        {
            var po = await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == linkedPoId, cancellationToken)
                ?? throw new KeyNotFoundException($"Purchase order {linkedPoId} not found");
            inheritedTermsId = po.PaymentTermsId ?? inheritedTermsId;
            var poCurrencyId = await PurchaseOrderCurrency.EffectiveCurrencyIdAsync(currencies, po, cancellationToken);
            if (request.CurrencyId is int requestedId && requestedId != poCurrencyId)
                throw DocumentCurrencyResolver.Failure("CurrencyId", DocumentCurrencyResolver.BillPoCurrencyMismatchCode,
                    $"A bill for PO {po.PONumber} must be in the PO's currency ({await currencies.CodeForAsync(poCurrencyId, cancellationToken) ?? po.QuoteCurrency}).");
            inheritedCurrencyId = poCurrencyId;
        }
        var currency = await currencies.ResolveAsync(
            request.CurrencyId, request.FxRate, inheritedCurrencyId, request.BillDate, cancellationToken);

        var bill = new VendorBill
        {
            BillNumber = billNumber,
            VendorId = request.VendorId,
            CurrencyId = currency.CurrencyId,
            FxRate = currency.FxRate,
            VendorInvoiceNumber = request.VendorInvoiceNumber,
            PurchaseOrderId = request.PurchaseOrderId,
            Status = VendorBillStatus.Draft,
            BillDate = request.BillDate,
            DueDate = request.DueDate,
            TaxAmount = request.TaxAmount,
            Notes = request.Notes,
            CreatedByUserId = db.CurrentUserId,
        };
        var termsResolver = new PaymentTermsResolver(db);
        var paymentTerms = request.PaymentTermsId is int pickedTermsId
            ? await termsResolver.RequireAsync(pickedTermsId, nameof(request.PaymentTermsId), cancellationToken)
            : await termsResolver.FindAsync(inheritedTermsId, cancellationToken);
        PaymentTermsResolver.Stamp(bill, paymentTerms);

        var lineNumber = 1;
        foreach (var line in request.Lines)
        {
            if (line.LineType == DocumentLineType.Freight)
            {
                var freightPoId = line.FreightForPurchaseOrderId ?? request.PurchaseOrderId;
                bill.Lines.Add(new VendorBillLine
                {
                    Description = string.IsNullOrWhiteSpace(line.Description) ? FreightLines.DefaultDescription : line.Description,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    LineNumber = lineNumber++,
                    LineType = DocumentLineType.Freight,
                    FreightForPurchaseOrderId = freightPoId,
                    AccountDeterminationKey = freightPoId is null && !string.IsNullOrWhiteSpace(line.AccountDeterminationKey)
                        ? line.AccountDeterminationKey!
                        : FreightInKey,
                    GlAccountId = freightPoId is null ? line.GlAccountId : null,
                });
                continue;
            }

            bill.Lines.Add(new VendorBillLine
            {
                PartId = line.PartId,
                PurchaseOrderLineId = line.PurchaseOrderLineId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineNumber = lineNumber++,
                AccountDeterminationKey = line.PurchaseOrderLineId is not null
                    ? GrniKey
                    : string.IsNullOrWhiteSpace(line.AccountDeterminationKey)
                        ? "OPERATING_EXPENSE"
                        : line.AccountDeterminationKey!,
                GlAccountId = line.GlAccountId,
            });
        }

        await repo.AddAsync(bill, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        // Activity (transactional entity → log on the bill): after the first save so the id is assigned.
        var money = await MoneyFormat.ForCurrencyIdAsync(db, bill.CurrencyId, cancellationToken);
        db.LogActivityAt(
            "created",
            $"Bill {bill.BillNumber} created for {vendor.CompanyName} — {money.Format(bill.Total)}"
            + (bill.PurchaseOrderId is int poId ? $" (PO {poId})" : string.Empty),
            ("VendorBill", bill.Id));
        await db.SaveChangesAsync(cancellationToken);

        return new VendorBillListItemModel(
            bill.Id, bill.BillNumber, bill.VendorId, vendor.CompanyName, bill.VendorInvoiceNumber,
            bill.Status.ToString(), bill.BillDate, bill.DueDate,
            bill.Total, bill.AmountPaid, bill.BalanceDue, bill.CreatedAt,
            CurrencyId: bill.CurrencyId,
            CurrencyCode: currency.Code,
            FxRate: bill.FxRate,
            TotalFunctional: FxMath.ToFunctional(bill.Total, bill.FxRate),
            PaymentTermsId: bill.PaymentTermsId,
            DiscountPercent: bill.DiscountPercent,
            DiscountDueDate: bill.DiscountDueDate);
    }
}
