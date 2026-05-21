using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Payments;

public record CreatePaymentCommand(
    int CustomerId,
    string Method,
    decimal Amount,
    DateTimeOffset PaymentDate,
    string? ReferenceNumber,
    string? Notes,
    List<CreatePaymentApplicationModel>? Applications) : IRequest<PaymentListItemModel>;

public class CreatePaymentValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Method).NotEmpty();
        When(x => x.Applications != null && x.Applications.Count > 0, () =>
        {
            RuleFor(x => x.Applications!.Sum(a => a.Amount))
                .LessThanOrEqualTo(x => x.Amount)
                .WithMessage("Applied amounts cannot exceed payment amount");
            RuleForEach(x => x.Applications).ChildRules(app =>
            {
                app.RuleFor(a => a.InvoiceId).GreaterThan(0);
                app.RuleFor(a => a.Amount).GreaterThan(0);
            });
        });
    }
}

public class CreatePaymentHandler(IPaymentRepository repo, ICustomerRepository customerRepo, IInvoiceRepository invoiceRepo, AppDbContext db)
    : IRequestHandler<CreatePaymentCommand, PaymentListItemModel>
{
    public async Task<PaymentListItemModel> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        var customer = await customerRepo.FindAsync(request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        var paymentNumber = await repo.GenerateNextPaymentNumberAsync(cancellationToken);
        var method = Enum.Parse<PaymentMethod>(request.Method, true);

        var payment = new Payment
        {
            PaymentNumber = paymentNumber,
            CustomerId = request.CustomerId,
            Method = method,
            Amount = request.Amount,
            PaymentDate = request.PaymentDate,
            ReferenceNumber = request.ReferenceNumber,
            Notes = request.Notes,
        };

        decimal appliedTotal = 0;

        if (request.Applications != null)
        {
            foreach (var app in request.Applications)
            {
                var invoice = await invoiceRepo.FindWithDetailsAsync(app.InvoiceId, cancellationToken)
                    ?? throw new KeyNotFoundException($"Invoice {app.InvoiceId} not found");

                // F-027: consume the canonical Invoice.BalanceDue (Total − AmountPaid) rather than
                // re-deriving the money formula here, so payment validation can't drift from the
                // invoice's reported balance once line-level discounts land.
                var balanceDue = invoice.BalanceDue;

                if (app.Amount > balanceDue)
                    throw new InvalidOperationException(
                        $"Application amount {app.Amount:C} exceeds invoice {invoice.InvoiceNumber} balance of {balanceDue:C}");

                payment.Applications.Add(new PaymentApplication
                {
                    InvoiceId = app.InvoiceId,
                    Amount = app.Amount,
                });

                appliedTotal += app.Amount;

                // Update invoice status
                var newBalance = balanceDue - app.Amount;
                if (newBalance <= 0)
                    invoice.Status = InvoiceStatus.Paid;
                else if (invoice.Status == InvoiceStatus.Sent || invoice.Status == InvoiceStatus.Overdue)
                    invoice.Status = InvoiceStatus.PartiallyPaid;

                // F-026: force the concurrency token modified so a concurrent SaveChanges on
                // the same invoice row collides and throws DbUpdateConcurrencyException.
                db.Entry(invoice).Property(i => i.Version).IsModified = true;
            }
        }

        await repo.AddAsync(payment, cancellationToken);

        try
        {
            await repo.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // A concurrent payment committed to this invoice between our read and write.
            // Reload each conflicted invoice (including navigations for balance calc) and
            // re-run the over-apply guard; throws InvalidOperationException → 409.
            foreach (var entry in ex.Entries.Where(e => e.Entity is Invoice))
            {
                await entry.ReloadAsync(cancellationToken);
                var inv = (Invoice)entry.Entity;
                await db.Entry(inv).Collection(i => i.PaymentApplications).LoadAsync(cancellationToken);
                await db.Entry(inv).Collection(i => i.Lines).LoadAsync(cancellationToken);

                var freshBalance = inv.BalanceDue;

                var matchingApp = request.Applications!.First(a => a.InvoiceId == inv.Id);
                if (matchingApp.Amount > freshBalance)
                    throw new InvalidOperationException(
                        $"Application amount {matchingApp.Amount:C} exceeds invoice {inv.InvoiceNumber} balance of {freshBalance:C}");
            }

            throw new InvalidOperationException("Concurrent payment conflict — please retry.");
        }

        return new PaymentListItemModel(
            payment.Id, payment.PaymentNumber, payment.CustomerId, customer.Name,
            payment.Method.ToString(), payment.Amount, appliedTotal,
            payment.Amount - appliedTotal, payment.PaymentDate,
            payment.ReferenceNumber, payment.CreatedAt);
    }
}
