namespace Forge.Core.Models;

public record UpdateUserRequestModel(
    string? FirstName,
    string? LastName,
    string? Initials,
    string? AvatarColor,
    bool? IsActive,
    string? Role);
