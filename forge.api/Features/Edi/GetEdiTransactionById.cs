using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record GetEdiTransactionByIdQuery(int Id) : IRequest<EdiTransactionDetailResponseModel>;

public class GetEdiTransactionByIdHandler(AppDbContext db)
    : IRequestHandler<GetEdiTransactionByIdQuery, EdiTransactionDetailResponseModel>
{
    public async Task<EdiTransactionDetailResponseModel> Handle(
        GetEdiTransactionByIdQuery request, CancellationToken cancellationToken)
    {
        var t = await db.EdiTransactions
            .AsNoTracking()
            .Include(t => t.TradingPartner)
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"EDI transaction {request.Id} not found");

        return new EdiTransactionDetailResponseModel
        {
            Id = t.Id,
            TradingPartnerId = t.TradingPartnerId,
            TradingPartnerName = t.TradingPartner.Name,
            Direction = t.Direction,
            TransactionSet = t.TransactionSet,
            ControlNumber = t.ControlNumber,
            GroupControlNumber = t.GroupControlNumber,
            TransactionControlNumber = t.TransactionControlNumber,
            Status = t.Status,
            RelatedEntityType = t.RelatedEntityType,
            RelatedEntityId = t.RelatedEntityId,
            ReceivedAt = t.ReceivedAt,
            ProcessedAt = t.ProcessedAt,
            ErrorMessage = t.ErrorMessage,
            ErrorDetailJson = t.ErrorDetailJson,
            RetryCount = t.RetryCount,
            LastRetryAt = t.LastRetryAt,
            IsAcknowledged = t.IsAcknowledged,
            AcknowledgmentTransactionId = t.AcknowledgmentTransactionId,
            PayloadSizeBytes = t.PayloadSizeBytes,
            RawPayload = t.RawPayload,
            ParsedDataJson = t.ParsedDataJson,
        };
    }
}
