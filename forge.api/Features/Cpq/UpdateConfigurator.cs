using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Cpq;

public record UpdateConfiguratorCommand(int Id, UpdateConfiguratorRequestModel Request) : IRequest;

public class UpdateConfiguratorHandler(AppDbContext db) : IRequestHandler<UpdateConfiguratorCommand>
{
    public async Task Handle(UpdateConfiguratorCommand command, CancellationToken cancellationToken)
    {
        var configurator = await db.ProductConfigurators
            .Include(c => c.Options)
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Configurator {command.Id} not found");

        var request = command.Request;
        configurator.Name = request.Name;
        configurator.Description = request.Description;
        configurator.BasePartId = request.BasePartId;
        configurator.BasePrice = request.BasePrice;
        configurator.IsActive = request.IsActive;
        configurator.ValidationRulesJson = request.ValidationRulesJson;
        configurator.PricingFormulaJson = request.PricingFormulaJson;

        if (request.Options is not null)
        {
            db.Set<ConfiguratorOption>().RemoveRange(configurator.Options);
            configurator.Options.Clear();

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

        await db.SaveChangesAsync(cancellationToken);
    }
}
