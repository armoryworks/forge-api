using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record RetryEdiTransactionCommand(int Id) : IRequest;

public class RetryEdiTransactionHandler(AppDbContext db, IEdiService ediService)
    : IRequestHandler<RetryEdiTransactionCommand>
{
    public async Task Handle(RetryEdiTransactionCommand request, CancellationToken cancellationToken)
    {
        var transaction = await db.EdiTransactions
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"EDI transaction {request.Id} not found");

        if (transaction.Status != EdiTransactionStatus.Error)
            throw new InvalidOperationException("Only failed transactions can be retried");

        transaction.RetryCount++;
        transaction.LastRetryAt = DateTimeOffset.UtcNow;
        transaction.Status = EdiTransactionStatus.Received;
        transaction.ErrorMessage = null;
        transaction.ErrorDetailJson = null;

        await db.SaveChangesAsync(cancellationToken);
        await ediService.RetryTransactionAsync(transaction.Id, cancellationToken);
    }
}
