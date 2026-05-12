namespace Forge.Core.Models;

public record UpdateEdiMappingRequestModel
{
    public string? TransactionSet { get; init; }
    public string? Name { get; init; }
    public string? FieldMappingsJson { get; init; }
    public string? ValueTranslationsJson { get; init; }
    public bool? IsDefault { get; init; }
    public string? Notes { get; init; }
}
