using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record DeleteConsignmentAgreementCommand(int Id) : IRequest;

public class DeleteConsignmentAgreementHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteConsignmentAgreementCommand>
{
    public async Task Handle(DeleteConsignmentAgreementCommand request, CancellationToken cancellationToken)
    {
        var agreement = await db.ConsignmentAgreements
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Consignment agreement {request.Id} not found");

        if (await db.BinContents.AnyAsync(c => c.ConsignmentAgreementId == agreement.Id && c.RemovedAt == null && c.Quantity > 0m, cancellationToken))
            throw new InvalidOperationException(
                $"Agreement CA-{agreement.Id} still has vendor-owned stock in our bins. Consume it or count it to zero first.");
        if (await db.ConsignmentTransactions.AnyAsync(t => t.AgreementId == agreement.Id
                && t.Type == ConsignmentTransactionType.Consumption && t.VendorBillId == null, cancellationToken))
            throw new InvalidOperationException(
                $"Agreement CA-{agreement.Id} has consumption not yet billed. Reconcile it first.");

        agreement.Status = ConsignmentAgreementStatus.Terminated;
        agreement.DeletedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }
}
