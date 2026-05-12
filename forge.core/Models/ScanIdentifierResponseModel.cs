namespace Forge.Core.Models;

public record ScanIdentifierResponseModel
{
    public int Id { get; init; }
    public string IdentifierType { get; init; } = string.Empty;
    public string IdentifierValue { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
