using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Invoices;

public record DeleteInvoiceCommand(int Id) : IRequest;

public class DeleteInvoiceHandler(IInvoiceRepository repo, AppDbContext db, IClock clock)
    : IRequestHandler<DeleteInvoiceCommand>
{
    public async Task Handle(DeleteInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Invoice {request.Id} not found");

        if (invoice.Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Only Draft invoices can be deleted");

        invoice.DeletedAt = clock.UtcNow;
        invoice.ShipmentId = null;

        db.LogActivityAt("deleted", $"Deleted draft invoice {invoice.InvoiceNumber}", ("Invoice", invoice.Id));
        await repo.SaveChangesAsync(cancellationToken);
    }
}
