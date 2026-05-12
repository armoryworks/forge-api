namespace Forge.Core.Models;

public record PpapLevelRequirementResponseModel
{
    public int ElementNumber { get; init; }
    public string ElementName { get; init; } = string.Empty;
    public string Requirement { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
}
