namespace Forge.Core.Models;

public record SystemSettingResponseModel(
    int Id,
    string Key,
    string Value,
    string? Description
);
