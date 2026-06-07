using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

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
    List<CreateVendorBillLineModel> Lines) : IRequest<VendorBillListItemModel>;

public class CreateVendorBillValidator : AbstractValidator<CreateVendorBillCommand>
{
    public CreateVendorBillValidator()
    {
        RuleFor(x => x.VendorId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleFor(x => x.TaxAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.BillDate);
        // A zero-total bill would flip to Approved yet post no journal (the engine skips non-positive
        // totals) — a silent status/ledger divergence. Require a positive total.
        RuleFor(x => x)
            .Must(c => c.Lines.Sum(l => l.Quantity * l.UnitPrice) + c.TaxAmount > 0m)
            .WithMessage("Bill total must be greater than zero")
            .When(x => x.Lines is { Count: > 0 });
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
        });
        // 3-way match (STAGE D): a PO-linked bill must match each line to a PO line so the posting can
        // clear GRNI at the PO price; a standalone (non-PO) bill must NOT carry PO-line links.
        When(x => x.PurchaseOrderId is not null, () =>
            RuleForEach(x => x.Lines).ChildRules(line =>
                line.RuleFor(l => l.PurchaseOrderLineId)
                    .NotNull()
                    .WithMessage("Each line on a PO-linked bill must reference a purchase-order line (3-way match).")));
        When(x => x.PurchaseOrderId is null, () =>
            RuleForEach(x => x.Lines).ChildRules(line =>
                line.RuleFor(l => l.PurchaseOrderLineId)
                    .Null()
                    .WithMessage("A standalone (non-PO) bill line cannot reference a purchase-order line.")));
    }
}

public class CreateVendorBillHandler(
    IVendorBillRepository repo,
    IVendorRepository vendorRepo)
    : IRequestHandler<CreateVendorBillCommand, VendorBillListItemModel>
{
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

        var billNumber = await repo.GenerateNextBillNumberAsync(cancellationToken);

        var bill = new VendorBill
        {
            BillNumber = billNumber,
            VendorId = request.VendorId,
            VendorInvoiceNumber = request.VendorInvoiceNumber,
            PurchaseOrderId = request.PurchaseOrderId,
            Status = VendorBillStatus.Draft,
            BillDate = request.BillDate,
            DueDate = request.DueDate,
            TaxAmount = request.TaxAmount,
            Notes = request.Notes,
        };

        var lineNumber = 1;
        foreach (var line in request.Lines)
        {
            bill.Lines.Add(new VendorBillLine
            {
                PartId = line.PartId,
                PurchaseOrderLineId = line.PurchaseOrderLineId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineNumber = lineNumber++,
                AccountDeterminationKey = string.IsNullOrWhiteSpace(line.AccountDeterminationKey)
                    ? "OPERATING_EXPENSE"
                    : line.AccountDeterminationKey!,
            });
        }

        await repo.AddAsync(bill, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        return new VendorBillListItemModel(
            bill.Id, bill.BillNumber, bill.VendorId, vendor.CompanyName, bill.VendorInvoiceNumber,
            bill.Status.ToString(), bill.BillDate, bill.DueDate,
            bill.Total, bill.AmountPaid, bill.BalanceDue, bill.CreatedAt);
    }
}
