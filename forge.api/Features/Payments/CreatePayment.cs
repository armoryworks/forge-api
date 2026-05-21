using FluentValidation;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

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

public class CreatePaymentHandler(IPaymentRepository repo, ICustomerRepository customerRepo, IInvoiceRepository invoiceRepo)
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

                // F-027: consume the canonical Invoice.BalanceDue rather than re-deriving the
                // money formula here. The two were numerically equal only while
                // InvoiceLine.LineTotal == Quantity * UnitPrice; a single source of truth keeps
                // payment validation and the invoice's reported balance from drifting apart.
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
            }
        }

        await repo.AddAsync(payment, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        return new PaymentListItemModel(
            payment.Id, payment.PaymentNumber, payment.CustomerId, customer.Name,
            payment.Method.ToString(), payment.Amount, appliedTotal,
            payment.Amount - appliedTotal, payment.PaymentDate,
            payment.ReferenceNumber, payment.CreatedAt);
    }
}
