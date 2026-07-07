using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Invoices;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PaymentSchedules;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — standalone mode only. Generates a one-line local
/// invoice for a milestone's derived amount; when an accounting provider is
/// connected, invoicing lives in the provider and this command refuses to run
/// (mirrors the connected-provider check in <see cref="CreateInvoiceHandler"/>).
/// Requires the schedule to be linked to a sales order — deposits are invoiced
/// against the order, never against the quote.
/// </summary>
public record GenerateMilestoneInvoiceCommand(int MilestoneId) : IRequest<InvoiceListItemModel>;

public class GenerateMilestoneInvoiceValidator : AbstractValidator<GenerateMilestoneInvoiceCommand>
{
    public GenerateMilestoneInvoiceValidator()
    {
        RuleFor(x => x.MilestoneId).GreaterThan(0);
    }
}

public class GenerateMilestoneInvoiceHandler(
    AppDbContext db,
    IMediator mediator,
    IClock clock,
    // Optional / null-default so isolated unit-test constructions stay valid (same
    // pattern as CreateInvoiceHandler, AUDIT-21-S1); the DI path supplies it.
    IAccountingService? accountingService = null)
    : IRequestHandler<GenerateMilestoneInvoiceCommand, InvoiceListItemModel>
{
    public async Task<InvoiceListItemModel> Handle(GenerateMilestoneInvoiceCommand request, CancellationToken cancellationToken)
    {
        // ⚡ Accounting boundary: a connected provider owns invoicing.
        if (accountingService is not null && await accountingService.TestConnectionAsync(cancellationToken))
            throw new InvalidOperationException(
                "Milestone invoices can only be generated in standalone mode — "
                + "invoicing is managed by the connected accounting provider.");

        var milestone = await db.PaymentMilestones
            .Include(m => m.PaymentSchedule)
            .FirstOrDefaultAsync(m => m.Id == request.MilestoneId, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment milestone {request.MilestoneId} not found");

        if (milestone.InvoiceId is not null || milestone.Status == PaymentMilestoneStatus.Invoiced)
            throw new InvalidOperationException("Milestone has already been invoiced");
        if (milestone.Status is PaymentMilestoneStatus.Paid or PaymentMilestoneStatus.Waived)
            throw new InvalidOperationException($"Cannot invoice a {milestone.Status} milestone");

        var schedule = milestone.PaymentSchedule;
        if (schedule.SalesOrderId is not int salesOrderId)
            throw new InvalidOperationException(
                "The payment schedule is not linked to a sales order yet — convert the quote before invoicing milestones");

        var salesOrder = await db.SalesOrders.AsNoTracking().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == salesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {salesOrderId} not found");

        var documentTotal = PaymentMilestoneEvaluator.QuantizeMoney(salesOrder.Total);
        var amount = PaymentMilestoneEvaluator.DeriveAmount(milestone, documentTotal);

        var now = clock.UtcNow;
        // FixedDate milestones keep their contractual due date (never before the
        // invoice date — CreateInvoiceValidator enforces DueDate ≥ InvoiceDate);
        // NetDays runs from the invoice; everything else is due on receipt.
        var dueDate = milestone.DueDate is { } fixedDue && fixedDue > now
            ? fixedDue
            : now.AddDays(milestone.NetDays ?? 0);

        // Reuse the canonical invoice-creation command (number generation, SO
        // linkage, over-invoicing guards — a no-PartId line is shipment-exempt).
        // TaxRate is 0: the milestone percentage applies to the tax-inclusive
        // document total, so the derived amount is already the gross figure.
        var invoice = await mediator.Send(new CreateInvoiceCommand(
            CustomerId: salesOrder.CustomerId,
            SalesOrderId: salesOrder.Id,
            ShipmentId: null,
            InvoiceDate: now,
            DueDate: dueDate,
            CreditTerms: null,
            TaxRate: 0m,
            Notes: $"Pre-payment milestone invoice for order {salesOrder.OrderNumber}",
            Lines:
            [
                new CreateInvoiceLineModel(
                    PartId: null,
                    Description: $"Payment milestone {milestone.Sequence}: {milestone.Name} ({milestone.Percentage:0.##}%)",
                    Quantity: 1m,
                    UnitPrice: amount),
            ]), cancellationToken);

        milestone.InvoiceId = invoice.Id;
        milestone.AmountLocked ??= amount;
        milestone.Status = PaymentMilestoneStatus.Invoiced;

        db.LogActivityAt(
            "payment-milestone-invoiced",
            $"Invoice {invoice.InvoiceNumber} generated for milestone {milestone.Sequence} "
            + $"'{milestone.Name}' — {amount:0.00}",
            ("SalesOrder", salesOrderId));

        await db.SaveChangesAsync(cancellationToken);

        return invoice;
    }
}
