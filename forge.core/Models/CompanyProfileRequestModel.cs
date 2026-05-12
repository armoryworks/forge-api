namespace Forge.Core.Models;

public record CompanyProfileRequestModel(
    string? Name,
    string? Phone,
    string? Email,
    string? Ein,
    string? Website);
