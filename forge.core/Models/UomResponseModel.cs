namespace Forge.Core.Models;

public record UomResponseModel(
    int Id, string Code, string Name, string? Symbol,
    string Category, int DecimalPlaces, bool IsBaseUnit, bool IsActive, int SortOrder);
