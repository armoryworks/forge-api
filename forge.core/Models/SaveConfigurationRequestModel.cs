namespace Forge.Core.Models;

public record SaveConfigurationRequestModel(
    int ConfiguratorId,
    Dictionary<string, string> Selections);
