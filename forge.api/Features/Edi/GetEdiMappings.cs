using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record GetEdiMappingsQuery(int TradingPartnerId) : IRequest<List<EdiMappingResponseModel>>;

public class GetEdiMappingsHandler(AppDbContext db)
    : IRequestHandler<GetEdiMappingsQuery, List<EdiMappingResponseModel>>
{
    public async Task<List<EdiMappingResponseModel>> Handle(
        GetEdiMappingsQuery request, CancellationToken cancellationToken)
    {
        return await db.EdiMappings
            .AsNoTracking()
            .Where(m => m.TradingPartnerId == request.TradingPartnerId)
            .OrderBy(m => m.TransactionSet)
            .ThenBy(m => m.Name)
            .Select(m => new EdiMappingResponseModel
            {
                Id = m.Id,
                TradingPartnerId = m.TradingPartnerId,
                TransactionSet = m.TransactionSet,
                Name = m.Name,
                FieldMappingsJson = m.FieldMappingsJson,
                ValueTranslationsJson = m.ValueTranslationsJson,
                IsDefault = m.IsDefault,
                Notes = m.Notes,
            })
            .ToListAsync(cancellationToken);
    }
}
