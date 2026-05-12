using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Cpq;

public record GetConfiguratorByIdQuery(int Id) : IRequest<ConfiguratorDetailResponseModel>;

public class GetConfiguratorByIdHandler(AppDbContext db) : IRequestHandler<GetConfiguratorByIdQuery, ConfiguratorDetailResponseModel>
{
    public async Task<ConfiguratorDetailResponseModel> Handle(GetConfiguratorByIdQuery request, CancellationToken cancellationToken)
    {
        var configurator = await db.ProductConfigurators
            .AsNoTracking()
            .Include(c => c.BasePart)
            .Include(c => c.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Configurator {request.Id} not found");

        return new ConfiguratorDetailResponseModel(
            configurator.Id,
            configurator.Name,
            configurator.Description,
            configurator.BasePartId,
            configurator.BasePart.PartNumber,
            configurator.IsActive,
            configurator.BasePrice,
            configurator.ValidationRulesJson,
            configurator.PricingFormulaJson,
            configurator.Options.Select(o => new ConfiguratorOptionResponseModel(
                o.Id, o.Name, o.OptionType, o.ValuesJson, o.PricingRuleJson,
                o.BomImpactJson, o.RoutingImpactJson, o.DependsOnOptionId,
                o.SortOrder, o.IsRequired, o.HelpText, o.DefaultValue)).ToList(),
            configurator.CreatedAt,
            configurator.UpdatedAt);
    }
}
