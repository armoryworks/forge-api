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
