using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockCpqService(ILogger<MockCpqService> logger) : ICpqService
{
    public Task<CpqResult> ConfigureAsync(int configuratorId, Dictionary<string, string> selections, CancellationToken ct)
    {
        logger.LogInformation("[MockCPQ] Configure configurator {ConfiguratorId} with {SelectionCount} selections",
            configuratorId, selections.Count);

        var breakdown = selections.Select(s => new CpqPriceBreakdown
        {
            OptionName = s.Key,
            Selection = s.Value,
            PriceImpact = 0m,
        }).ToList();

        return Task.FromResult(new CpqResult
        {
            ComputedPrice = 100m,
            PriceBreakdown = breakdown,
            BomPreview = [],
            RoutingPreview = [],
            ValidationErrors = [],
            IsValid = true,
        });
    }

    public Task<bool> ValidateSelectionsAsync(int configuratorId, Dictionary<string, string> selections, CancellationToken ct)
    {
        logger.LogInformation("[MockCPQ] Validate selections for configurator {ConfiguratorId}", configuratorId);
        return Task.FromResult(true);
    }

    public Task<Quote> GenerateQuoteFromConfigurationAsync(int configurationId, int customerId, CancellationToken ct)
    {
        logger.LogInformation("[MockCPQ] Generate quote from configuration {ConfigurationId} for customer {CustomerId}",
            configurationId, customerId);
        throw new NotImplementedException("Mock CPQ does not generate real quotes");
    }

    public Task<Part> GeneratePartFromConfigurationAsync(int configurationId, CancellationToken ct)
    {
        logger.LogInformation("[MockCPQ] Generate part from configuration {ConfigurationId}", configurationId);
        throw new NotImplementedException("Mock CPQ does not generate real parts");
    }

    public decimal CalculatePrice(ProductConfigurator configurator, Dictionary<string, string> selections)
    {
        logger.LogInformation("[MockCPQ] Calculate price for {ConfiguratorName}", configurator.Name);
        return configurator.BasePrice ?? 0m;
    }

    public IReadOnlyList<BOMLine> GenerateBom(ProductConfigurator configurator, Dictionary<string, string> selections)
    {
        logger.LogInformation("[MockCPQ] Generate BOM for {ConfiguratorName}", configurator.Name);
        return [];
    }

    public IReadOnlyList<Operation> GenerateRouting(ProductConfigurator configurator, Dictionary<string, string> selections)
    {
        logger.LogInformation("[MockCPQ] Generate routing for {ConfiguratorName}", configurator.Name);
        return [];
    }
}
