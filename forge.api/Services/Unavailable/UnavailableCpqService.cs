using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services.Unavailable;

/// <summary>
/// The configure-price-quote engine outside mock posture. No real engine exists yet, so every operation
/// answers 501 rather than the mock's fixed price or its NotImplementedException.
/// </summary>
public sealed class UnavailableCpqService : ICpqService
{
    public const string Message = "Product configuration (CPQ) is not available in this version of Forge.";

    public Task<CpqResult> ConfigureAsync(int configuratorId, Dictionary<string, string> selections, decimal quantity, CancellationToken ct) =>
        throw new FeatureNotAvailableException(Message);

    public Task<bool> ValidateSelectionsAsync(int configuratorId, Dictionary<string, string> selections, CancellationToken ct) =>
        throw new FeatureNotAvailableException(Message);

    public Task<Quote> GenerateQuoteFromConfigurationAsync(int configurationId, int customerId, CancellationToken ct) =>
        throw new FeatureNotAvailableException(Message);

    public Task<Part> GeneratePartFromConfigurationAsync(int configurationId, CancellationToken ct) =>
        throw new FeatureNotAvailableException(Message);

    public decimal CalculatePrice(ProductConfigurator configurator, Dictionary<string, string> selections, decimal quantity) =>
        throw new FeatureNotAvailableException(Message);

    public IReadOnlyList<BOMLine> GenerateBom(ProductConfigurator configurator, Dictionary<string, string> selections) =>
        throw new FeatureNotAvailableException(Message);

    public IReadOnlyList<Operation> GenerateRouting(ProductConfigurator configurator, Dictionary<string, string> selections) =>
        throw new FeatureNotAvailableException(Message);
}
