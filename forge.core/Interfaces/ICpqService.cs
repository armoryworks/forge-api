using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ICpqService
{
    Task<CpqResult> ConfigureAsync(int configuratorId, Dictionary<string, string> selections, CancellationToken ct);
    Task<bool> ValidateSelectionsAsync(int configuratorId, Dictionary<string, string> selections, CancellationToken ct);
    Task<Quote> GenerateQuoteFromConfigurationAsync(int configurationId, int customerId, CancellationToken ct);
    Task<Part> GeneratePartFromConfigurationAsync(int configurationId, CancellationToken ct);
    decimal CalculatePrice(ProductConfigurator configurator, Dictionary<string, string> selections);
    IReadOnlyList<BOMLine> GenerateBom(ProductConfigurator configurator, Dictionary<string, string> selections);
    IReadOnlyList<Operation> GenerateRouting(ProductConfigurator configurator, Dictionary<string, string> selections);
}
