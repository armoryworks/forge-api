using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Invoices;

public record SendInvoiceCommand(int Id) : IRequest;

public class SendInvoiceHandler(IInvoiceRepository repo)
    : IRequestHandler<SendInvoiceCommand>
{
    public async Task Handle(SendInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Invoice {request.Id} not found");

        if (invoice.Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Only Draft invoices can be sent");

        invoice.Status = InvoiceStatus.Sent;

        await repo.SaveChangesAsync(cancellationToken);
    }
}
