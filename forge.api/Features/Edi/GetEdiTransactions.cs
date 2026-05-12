using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record GetEdiTransactionsQuery(
    EdiDirection? Direction,
    string? TransactionSet,
    EdiTransactionStatus? Status,
    int? TradingPartnerId,
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    int Page = 1,
    int PageSize = 25
) : IRequest<PaginatedResponse<EdiTransactionResponseModel>>;

public record PaginatedResponse<T>(List<T> Data, int Page, int PageSize, int TotalCount, int TotalPages);

public class GetEdiTransactionsHandler(AppDbContext db)
    : IRequestHandler<GetEdiTransactionsQuery, PaginatedResponse<EdiTransactionResponseModel>>
{
    public async Task<PaginatedResponse<EdiTransactionResponseModel>> Handle(
        GetEdiTransactionsQuery request, CancellationToken cancellationToken)
    {
        var query = db.EdiTransactions
            .AsNoTracking()
            .Include(t => t.TradingPartner)
            .AsQueryable();

        if (request.Direction.HasValue)
            query = query.Where(t => t.Direction == request.Direction.Value);
        if (!string.IsNullOrEmpty(request.TransactionSet))
            query = query.Where(t => t.TransactionSet == request.TransactionSet);
        if (request.Status.HasValue)
            query = query.Where(t => t.Status == request.Status.Value);
        if (request.TradingPartnerId.HasValue)
            query = query.Where(t => t.TradingPartnerId == request.TradingPartnerId.Value);
        if (request.DateFrom.HasValue)
            query = query.Where(t => t.ReceivedAt >= request.DateFrom.Value);
        if (request.DateTo.HasValue)
            query = query.Where(t => t.ReceivedAt <= request.DateTo.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var transactions = await query
            .OrderByDescending(t => t.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new EdiTransactionResponseModel
            {
                Id = t.Id,
                TradingPartnerId = t.TradingPartnerId,
                TradingPartnerName = t.TradingPartner.Name,
                Direction = t.Direction,
                TransactionSet = t.TransactionSet,
                ControlNumber = t.ControlNumber,
                Status = t.Status,
                RelatedEntityType = t.RelatedEntityType,
                RelatedEntityId = t.RelatedEntityId,
                ReceivedAt = t.ReceivedAt,
                ProcessedAt = t.ProcessedAt,
                ErrorMessage = t.ErrorMessage,
                RetryCount = t.RetryCount,
                IsAcknowledged = t.IsAcknowledged,
                PayloadSizeBytes = t.PayloadSizeBytes,
            })
            .ToListAsync(cancellationToken);

        return new PaginatedResponse<EdiTransactionResponseModel>(transactions, page, pageSize, totalCount, totalPages);
    }
}
