using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetLaborRatesQuery(int UserId) : IRequest<List<LaborRateResponseModel>>;

public class GetLaborRatesHandler(AppDbContext db)
    : IRequestHandler<GetLaborRatesQuery, List<LaborRateResponseModel>>
{
    public async Task<List<LaborRateResponseModel>> Handle(
        GetLaborRatesQuery request, CancellationToken cancellationToken)
    {
        return await db.LaborRates
            .AsNoTracking()
            .Where(r => r.UserId == request.UserId)
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => new LaborRateResponseModel
            {
                Id = r.Id,
                UserId = r.UserId,
                StandardRatePerHour = r.StandardRatePerHour,
                OvertimeRatePerHour = r.OvertimeRatePerHour,
                DoubletimeRatePerHour = r.DoubletimeRatePerHour,
                EffectiveFrom = r.EffectiveFrom,
                EffectiveTo = r.EffectiveTo,
                Notes = r.Notes,
            })
            .ToListAsync(cancellationToken);
    }
}
