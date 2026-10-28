namespace Forge.Core.Models;

public record JobSurplusSplitResponseModel(
    decimal DemandQuantity,
    decimal SurplusQuantity,
    int? SurplusLocationId,
    string? SurplusLocationName);
