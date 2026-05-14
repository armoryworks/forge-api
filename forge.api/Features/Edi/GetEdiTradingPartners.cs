using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record GetEdiTradingPartnersQuery(bool? IsActive) : IRequest<List<EdiTradingPartnerResponseModel>>;

public class GetEdiTradingPartnersHandler(AppDbContext db)
    : IRequestHandler<GetEdiTradingPartnersQuery, List<EdiTradingPartnerResponseModel>>
{
    public async Task<List<EdiTradingPartnerResponseModel>> Handle(
        GetEdiTradingPartnersQuery request, CancellationToken cancellationToken)
    {
        var query = db.EdiTradingPartners
            .AsNoTracking()
            .Include(p => p.Customer)
            .Include(p => p.Vendor)
            .AsQueryable();

        if (request.IsActive.HasValue)
            query = query.Where(p => p.IsActive == request.IsActive.Value);

        var partners = await query
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var partnerIds = partners.Select(p => p.Id).ToList();

        var transactionStats = await db.EdiTransactions
            .AsNoTracking()
            .Where(t => partnerIds.Contains(t.TradingPartnerId))
            .GroupBy(t => t.TradingPartnerId)
            .Select(g => new
            {
                PartnerId = g.Key,
                Count = g.Count(),
                LastAt = g.Max(t => t.ReceivedAt),
                ErrorCount = g.Count(t => t.Status == EdiTransactionStatus.Error)
            })
            .ToDictionaryAsync(s => s.PartnerId, cancellationToken);

        return partners.Select(p =>
        {
            transactionStats.TryGetValue(p.Id, out var stats);
            return new EdiTradingPartnerResponseModel
            {
                Id = p.Id,
                Name = p.Name,
                CustomerId = p.CustomerId,
                CustomerName = p.Customer?.CompanyName,
                VendorId = p.VendorId,
                VendorName = p.Vendor?.CompanyName,
                QualifierId = p.QualifierId,
                QualifierValue = p.QualifierValue,
                DefaultFormat = p.DefaultFormat,
                TransportMethod = p.TransportMethod,
                AutoProcess = p.AutoProcess,
                RequireAcknowledgment = p.RequireAcknowledgment,
                IsActive = p.IsActive,
                Notes = p.Notes,
                TransactionCount = stats?.Count ?? 0,
                LastTransactionAt = stats?.LastAt,
                ErrorCount = stats?.ErrorCount ?? 0,
            };
        }).ToList();
    }
}
