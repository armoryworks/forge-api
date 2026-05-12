namespace Forge.Core.Models;

public record CreateEdiMappingRequestModel
{
    public string TransactionSet { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FieldMappingsJson { get; init; } = "[]";
    public string ValueTranslationsJson { get; init; } = "[]";
    public bool IsDefault { get; init; }
    public string? Notes { get; init; }
}
