namespace Forge.Core.Models;

public record ImportTranslationsRequestModel(
    Dictionary<string, string> Translations);
