using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public interface ILocalizationService
{
    Task<string> GetLabelAsync(string key, string languageCode, CancellationToken ct);
    Task<Dictionary<string, string>> GetAllLabelsAsync(string languageCode, CancellationToken ct);
    Task SetLabelAsync(string key, string languageCode, string value, CancellationToken ct);
    Task<IReadOnlyList<SupportedLanguage>> GetSupportedLanguagesAsync(CancellationToken ct);
    Task ImportTranslationsAsync(string languageCode, Dictionary<string, string> translations, CancellationToken ct);
    Task<Dictionary<string, string>> ExportTranslationsAsync(string languageCode, CancellationToken ct);
}
