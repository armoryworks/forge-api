using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ECommerce;

public record GetChannelSettlementByIdQuery(int Id) : IRequest<ChannelSettlementDetailResponseModel>;

public class GetChannelSettlementByIdHandler(AppDbContext db)
    : IRequestHandler<GetChannelSettlementByIdQuery, ChannelSettlementDetailResponseModel>
{
    public async Task<ChannelSettlementDetailResponseModel> Handle(
        GetChannelSettlementByIdQuery request, CancellationToken ct)
    {
        var header = await db.ChannelSettlements
            .AsNoTracking()
            .Where(s => s.Id == request.Id)
            .Select(s => new ChannelSettlementResponseModel
            {
                Id = s.Id,
                ChannelId = s.ChannelId,
                ChannelName = s.Channel.Name,
                ExternalSettlementId = s.ExternalSettlementId,
                PeriodStart = s.PeriodStart,
                PeriodEnd = s.PeriodEnd,
                DepositedAt = s.DepositedAt,
                ReportedNetAmount = s.ReportedNetAmount,
                ComputedNetAmount = s.Lines.Sum(l => (decimal?)l.Amount) ?? 0m,
                Variance = s.ReportedNetAmount - (s.Lines.Sum(l => (decimal?)l.Amount) ?? 0m),
                CurrencyCode = s.CurrencyCode,
                Status = s.Status,
                ResolutionNotes = s.ResolutionNotes,
                LineCount = s.Lines.Count,
                UnmatchedLineCount = s.Lines.Count(l => l.ExternalOrderId != null && l.SalesOrderId == null),
                CreatedAt = s.CreatedAt,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"ChannelSettlement {request.Id} not found");

        var lines = await db.ChannelSettlementLines
            .AsNoTracking()
            .Where(l => l.SettlementId == request.Id)
            // Unmatched first, then by type, so the exceptions are at the top of
            // the detail rather than buried mid-batch.
            .OrderBy(l => l.ExternalOrderId != null && l.SalesOrderId == null ? 0 : 1)
            .ThenBy(l => l.LineType)
            .ThenBy(l => l.Id)
            .Select(l => new ChannelSettlementLineResponseModel
            {
                Id = l.Id,
                LineType = l.LineType,
                SalesOrderId = l.SalesOrderId,
                SalesOrderNumber = l.SalesOrder == null ? null : l.SalesOrder.OrderNumber,
                ExternalOrderId = l.ExternalOrderId,
                Amount = l.Amount,
                Description = l.Description,
                PostedAt = l.PostedAt,
                IsUnmatched = l.ExternalOrderId != null && l.SalesOrderId == null,
            })
            .ToListAsync(ct);

        return new ChannelSettlementDetailResponseModel { Settlement = header, Lines = lines };
    }
}
