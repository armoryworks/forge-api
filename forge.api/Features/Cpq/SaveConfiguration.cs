using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Cpq;

public record SaveConfigurationCommand(SaveConfigurationRequestModel Request) : IRequest<ProductConfigurationResponseModel>;

public class SaveConfigurationHandler(AppDbContext db, ICpqService cpqService, IClock clock) : IRequestHandler<SaveConfigurationCommand, ProductConfigurationResponseModel>
{
    public async Task<ProductConfigurationResponseModel> Handle(SaveConfigurationCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        var configurator = await db.ProductConfigurators
            .AsNoTracking()
            .Include(c => c.Options)
            .FirstOrDefaultAsync(c => c.Id == request.ConfiguratorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Configurator {request.ConfiguratorId} not found");

        var result = await cpqService.ConfigureAsync(request.ConfiguratorId, request.Selections, cancellationToken);
        if (!result.IsValid)
            throw new InvalidOperationException("Configuration has validation errors: " + string.Join(", ", result.ValidationErrors));

        var now = clock.UtcNow;
        var code = $"CFG-{now:yyyyMMdd}-{now:HHmmss}";

        var configuration = new ProductConfiguration
        {
            ConfiguratorId = request.ConfiguratorId,
            ConfigurationCode = code,
            SelectionsJson = JsonSerializer.Serialize(request.Selections),
            ComputedPrice = result.ComputedPrice,
            GeneratedBomJson = JsonSerializer.Serialize(result.BomPreview),
            GeneratedRoutingJson = JsonSerializer.Serialize(result.RoutingPreview),
        };

        db.ProductConfigurations.Add(configuration);
        await db.SaveChangesAsync(cancellationToken);

        return new ProductConfigurationResponseModel(
            configuration.Id, configuration.ConfiguratorId, configurator.Name,
            configuration.ConfigurationCode, configuration.SelectionsJson,
            configuration.ComputedPrice, configuration.GeneratedBomJson,
            configuration.GeneratedRoutingJson, configuration.QuoteId,
            configuration.PartId, configuration.Status,
            configuration.CreatedAt, configuration.UpdatedAt);
    }
}
