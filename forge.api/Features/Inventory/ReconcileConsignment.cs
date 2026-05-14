using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record ReconcileConsignmentCommand(int AgreementId, ReconcileConsignmentRequestModel Request) : IRequest<ConsignmentReconciliationResponseModel>;

public class ReconcileConsignmentHandler(AppDbContext db, IClock clock) : IRequestHandler<ReconcileConsignmentCommand, ConsignmentReconciliationResponseModel>
{
    public async Task<ConsignmentReconciliationResponseModel> Handle(ReconcileConsignmentCommand command, CancellationToken cancellationToken)
    {
        var agreement = await db.ConsignmentAgreements
            .FirstOrDefaultAsync(a => a.Id == command.AgreementId, cancellationToken)
            ?? throw new KeyNotFoundException($"Consignment agreement {command.AgreementId} not found");

        var transactions = await db.ConsignmentTransactions
            .AsNoTracking()
            .Where(t => t.AgreementId == command.AgreementId)
            .ToListAsync(cancellationToken);

        var bookQuantity = transactions
            .Where(t => t.Type == ConsignmentTransactionType.Receipt || t.Type == ConsignmentTransactionType.Adjustment)
            .Sum(t => t.Quantity)
            - transactions
            .Where(t => t.Type == ConsignmentTransactionType.Consumption || t.Type == ConsignmentTransactionType.Return)
            .Sum(t => t.Quantity);

        var variance = command.Request.PhysicalCount - bookQuantity;
        int? adjustmentId = null;

        if (variance != 0)
        {
            var adjustment = new ConsignmentTransaction
            {
                AgreementId = agreement.Id,
                Type = ConsignmentTransactionType.Reconciliation,
                Quantity = variance,
                UnitPrice = agreement.AgreedUnitPrice,
                ExtendedAmount = variance * agreement.AgreedUnitPrice,
                Notes = $"Reconciliation adjustment: book={bookQuantity}, physical={command.Request.PhysicalCount}",
                CreatedAt = clock.UtcNow,
            };

            db.ConsignmentTransactions.Add(adjustment);
            await db.SaveChangesAsync(cancellationToken);
            adjustmentId = adjustment.Id;
        }

        return new ConsignmentReconciliationResponseModel
        {
            AgreementId = agreement.Id,
            BookQuantity = bookQuantity,
            PhysicalQuantity = command.Request.PhysicalCount,
            Variance = variance,
            AdjustmentTransactionId = adjustmentId,
        };
    }
}
