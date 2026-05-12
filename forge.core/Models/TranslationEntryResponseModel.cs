namespace Forge.Core.Models;

public record TranslationEntryResponseModel(
    string Key,
    string Value,
    string? Context,
    bool IsApproved);
