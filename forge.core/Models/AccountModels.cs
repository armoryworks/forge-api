namespace Forge.Core.Models;

public record AccountResponseModel(
    int Id,
    string Name,
    string? Description,
    string? Industry,
    string? Website,
    string? Phone,
    string? Address,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    string? SizeBracket,
    int? OwnerUserId,
    int ContactCount,
    int LeadCount,
    DateTimeOffset CreatedAt);

public record AccountContactResponseModel(
    int Id,
    int AccountId,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Role,
    bool IsPrimary);

public record CreateAccountRequest(
    string Name,
    string? Description,
    string? Industry,
    string? Website,
    string? Phone,
    string? Address,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    string? SizeBracket);

public record UpdateAccountRequest(
    string Name,
    string? Description,
    string? Industry,
    string? Website,
    string? Phone,
    string? Address,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    string? SizeBracket,
    int? OwnerUserId);

public record UpsertAccountContactRequest(
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Role,
    bool IsPrimary);
