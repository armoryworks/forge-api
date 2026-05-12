using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Cpq;

public record GetConfigurationByIdQuery(int Id) : IRequest<ProductConfigurationResponseModel>;

public class GetConfigurationByIdHandler(AppDbContext db) : IRequestHandler<GetConfigurationByIdQuery, ProductConfigurationResponseModel>
{
    public async Task<ProductConfigurationResponseModel> Handle(GetConfigurationByIdQuery request, CancellationToken cancellationToken)
    {
        var config = await db.ProductConfigurations
            .AsNoTracking()
            .Include(c => c.Configurator)
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Configuration {request.Id} not found");

        return new ProductConfigurationResponseModel(
            config.Id, config.ConfiguratorId, config.Configurator.Name,
            config.ConfigurationCode, config.SelectionsJson,
            config.ComputedPrice, config.GeneratedBomJson,
            config.GeneratedRoutingJson, config.QuoteId,
            config.PartId, config.Status,
            config.CreatedAt, config.UpdatedAt);
    }
}
