using MediatR;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Cpq;

public record CreateConfiguratorCommand(CreateConfiguratorRequestModel Request) : IRequest<ConfiguratorResponseModel>;

public class CreateConfiguratorHandler(AppDbContext db) : IRequestHandler<CreateConfiguratorCommand, ConfiguratorResponseModel>
{
    public async Task<ConfiguratorResponseModel> Handle(CreateConfiguratorCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        var part = await db.Parts.FindAsync(new object[] { request.BasePartId }, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.BasePartId} not found");

        var configurator = new ProductConfigurator
        {
            Name = request.Name,
            Description = request.Description,
            BasePartId = request.BasePartId,
            BasePrice = request.BasePrice,
            ValidationRulesJson = request.ValidationRulesJson,
            PricingFormulaJson = request.PricingFormulaJson,
        };

        if (request.Options is { Count: > 0 })
        {
            foreach (var opt in request.Options)
            {
                configurator.Options.Add(new ConfiguratorOption
                {
                    Name = opt.Name,
                    OptionType = opt.OptionType,
                    ValuesJson = opt.ValuesJson,
                    PricingRuleJson = opt.PricingRuleJson,
                    BomImpactJson = opt.BomImpactJson,
                    RoutingImpactJson = opt.RoutingImpactJson,
                    DependsOnOptionId = opt.DependsOnOptionId,
                    SortOrder = opt.SortOrder,
                    IsRequired = opt.IsRequired,
                    HelpText = opt.HelpText,
                    DefaultValue = opt.DefaultValue,
                });
            }
        }

        db.ProductConfigurators.Add(configurator);
        await db.SaveChangesAsync(cancellationToken);

        return new ConfiguratorResponseModel(
            configurator.Id, configurator.Name, configurator.Description,
            configurator.BasePartId, part.PartNumber, configurator.IsActive,
            configurator.BasePrice, configurator.Options.Count,
            configurator.CreatedAt, configurator.UpdatedAt);
    }
}
