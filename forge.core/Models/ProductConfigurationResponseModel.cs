using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ProductConfigurationResponseModel(
    int Id,
    int ConfiguratorId,
    string ConfiguratorName,
    string ConfigurationCode,
    string SelectionsJson,
    decimal ComputedPrice,
    string? GeneratedBomJson,
    string? GeneratedRoutingJson,
    int? QuoteId,
    int? PartId,
    ConfigurationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
