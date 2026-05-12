namespace Forge.Core.Models;

public record ActivityResponseModel(
    int Id,
    string Action,
    string? FieldName,
    string? OldValue,
    string? NewValue,
    string Description,
    string? UserInitials,
    string? UserName,
    DateTimeOffset CreatedAt);
