using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record RecordConsignmentReceiptCommand(int AgreementId, RecordConsignmentTransactionRequestModel Request) : IRequest<ConsignmentTransactionResponseModel>;

public class RecordConsignmentReceiptHandler(AppDbContext db, IClock clock) : IRequestHandler<RecordConsignmentReceiptCommand, ConsignmentTransactionResponseModel>
{
    public async Task<ConsignmentTransactionResponseModel> Handle(RecordConsignmentReceiptCommand command, CancellationToken cancellationToken)
    {
        var agreement = await db.ConsignmentAgreements
            .FirstOrDefaultAsync(a => a.Id == command.AgreementId, cancellationToken)
            ?? throw new KeyNotFoundException($"Consignment agreement {command.AgreementId} not found");

        var transaction = new ConsignmentTransaction
        {
            AgreementId = agreement.Id,
            Type = ConsignmentTransactionType.Receipt,
            Quantity = command.Request.Quantity,
            UnitPrice = agreement.AgreedUnitPrice,
            ExtendedAmount = command.Request.Quantity * agreement.AgreedUnitPrice,
            Notes = command.Request.Notes,
            CreatedAt = clock.UtcNow,
        };

        db.ConsignmentTransactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);

        return new ConsignmentTransactionResponseModel
        {
            Id = transaction.Id,
            AgreementId = transaction.AgreementId,
            Type = transaction.Type,
            Quantity = transaction.Quantity,
            UnitPrice = transaction.UnitPrice,
            ExtendedAmount = transaction.ExtendedAmount,
            Notes = transaction.Notes,
            CreatedAt = transaction.CreatedAt,
        };
    }
}
