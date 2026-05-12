namespace Forge.Core.Models;

public record EmployeeListItemResponseModel(
    int Id,
    string FirstName,
    string LastName,
    string? Initials,
    string? AvatarColor,
    string Email,
    string? Phone,
    string Role,
    string? TeamName,
    int? TeamId,
    bool IsActive,
    string? JobTitle,
    string? Department,
    DateTimeOffset? StartDate,
    DateTimeOffset CreatedAt);
