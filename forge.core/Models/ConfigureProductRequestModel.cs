namespace Forge.Core.Models;

public record ConfigureProductRequestModel(
    int ConfiguratorId,
    Dictionary<string, string> Selections,
    decimal? Quantity = null);
