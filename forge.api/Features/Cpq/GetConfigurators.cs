using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Cpq;

public record GetConfiguratorsQuery(bool? IsActive = null, int? BasePartId = null) : IRequest<List<ConfiguratorResponseModel>>;

public class GetConfiguratorsHandler(AppDbContext db) : IRequestHandler<GetConfiguratorsQuery, List<ConfiguratorResponseModel>>
{
    public async Task<List<ConfiguratorResponseModel>> Handle(GetConfiguratorsQuery request, CancellationToken cancellationToken)
    {
        var query = db.ProductConfigurators
            .AsNoTracking()
            .Include(c => c.BasePart)
            .AsQueryable();

        if (request.IsActive.HasValue)
            query = query.Where(c => c.IsActive == request.IsActive.Value);

        if (request.BasePartId.HasValue)
            query = query.Where(c => c.BasePartId == request.BasePartId.Value);

        var configurators = await query
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return configurators.Select(c => new ConfiguratorResponseModel(
            c.Id,
            c.Name,
            c.Description,
            c.BasePartId,
            c.BasePart.PartNumber,
            c.IsActive,
            c.BasePrice,
            c.Options.Count,
            c.CreatedAt,
            c.UpdatedAt)).ToList();
    }
}
