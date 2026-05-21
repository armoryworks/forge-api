using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Invoices;

public record VoidInvoiceCommand(int Id) : IRequest;

public class VoidInvoiceHandler(IInvoiceRepository repo)
    : IRequestHandler<VoidInvoiceCommand>
{
    // F-033: source-state whitelist — Draft invoices should be deleted not voided;
    // PartiallyPaid is allowed here (payment guard below still catches applied payments)
    private static readonly HashSet<InvoiceStatus> _voidableStatuses =
        [InvoiceStatus.Sent, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue];

    public async Task Handle(VoidInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Invoice {request.Id} not found");

        if (!_voidableStatuses.Contains(invoice.Status))
            throw new InvalidOperationException(
                $"Cannot void an invoice in status {invoice.Status}. Allowed: Sent, PartiallyPaid, Overdue.");

        if (invoice.PaymentApplications.Any())
            throw new InvalidOperationException("Cannot void an invoice with payments applied");

        invoice.Status = InvoiceStatus.Voided;

        await repo.SaveChangesAsync(cancellationToken);
    }
}
