namespace Forge.Core.Models;

public record CreateUomRequestModel(
    string Code, string Name, string? Symbol,
    string Category, int DecimalPlaces, bool IsBaseUnit, int SortOrder);
