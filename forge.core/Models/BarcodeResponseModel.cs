namespace Forge.Core.Models;

public record BarcodeResponseModel(
    int Id,
    string Value,
    string EntityType,
    bool IsActive,
    DateTimeOffset CreatedAt,
    string Source = "System",
    string IdentityType = "Internal");
