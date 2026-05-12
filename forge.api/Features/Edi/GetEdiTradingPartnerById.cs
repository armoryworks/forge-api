using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record GetEdiTradingPartnerByIdQuery(int Id) : IRequest<EdiTradingPartnerResponseModel>;

public class GetEdiTradingPartnerByIdHandler(AppDbContext db)
    : IRequestHandler<GetEdiTradingPartnerByIdQuery, EdiTradingPartnerResponseModel>
{
    public async Task<EdiTradingPartnerResponseModel> Handle(
        GetEdiTradingPartnerByIdQuery request, CancellationToken cancellationToken)
    {
        var partner = await db.EdiTradingPartners
            .AsNoTracking()
            .Include(p => p.Customer)
            .Include(p => p.Vendor)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Trading partner {request.Id} not found");

        var stats = await db.EdiTransactions
            .AsNoTracking()
            .Where(t => t.TradingPartnerId == partner.Id)
            .GroupBy(t => 1)
            .Select(g => new
            {
                Count = g.Count(),
                LastAt = g.Max(t => t.ReceivedAt),
                ErrorCount = g.Count(t => t.Status == Core.Enums.EdiTransactionStatus.Error)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new EdiTradingPartnerResponseModel
        {
            Id = partner.Id,
            Name = partner.Name,
            CustomerId = partner.CustomerId,
            CustomerName = partner.Customer?.CompanyName,
            VendorId = partner.VendorId,
            VendorName = partner.Vendor?.CompanyName,
            QualifierId = partner.QualifierId,
            QualifierValue = partner.QualifierValue,
            DefaultFormat = partner.DefaultFormat,
            TransportMethod = partner.TransportMethod,
            AutoProcess = partner.AutoProcess,
            RequireAcknowledgment = partner.RequireAcknowledgment,
            IsActive = partner.IsActive,
            Notes = partner.Notes,
            TransactionCount = stats?.Count ?? 0,
            LastTransactionAt = stats?.LastAt,
            ErrorCount = stats?.ErrorCount ?? 0,
        };
    }
}
