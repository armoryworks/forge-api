namespace Forge.Core.Models;

public record CreateUserRequestModel(
    string Email,
    string FirstName,
    string LastName,
    string? Initials,
    string? AvatarColor,
    string Password,
    string Role);
