namespace Forge.Core.Models;

public record SupportedLanguageResponseModel(
    int Id,
    string Code,
    string Name,
    string NativeName,
    bool IsDefault,
    bool IsActive,
    decimal CompletionPercent);
