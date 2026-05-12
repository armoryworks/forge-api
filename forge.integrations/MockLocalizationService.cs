using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Integrations;

public class MockLocalizationService(ILogger<MockLocalizationService> logger) : ILocalizationService
{
    public Task<string> GetLabelAsync(string key, string languageCode, CancellationToken ct)
    {
        logger.LogInformation("[MockLocalization] GetLabel key={Key} lang={Lang}", key, languageCode);
        return Task.FromResult(key);
    }

    public Task<Dictionary<string, string>> GetAllLabelsAsync(string languageCode, CancellationToken ct)
    {
        logger.LogInformation("[MockLocalization] GetAllLabels lang={Lang}", languageCode);
        return Task.FromResult(new Dictionary<string, string>());
    }

    public Task SetLabelAsync(string key, string languageCode, string value, CancellationToken ct)
    {
        logger.LogInformation("[MockLocalization] SetLabel key={Key} lang={Lang} value={Value}", key, languageCode, value);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SupportedLanguage>> GetSupportedLanguagesAsync(CancellationToken ct)
    {
        logger.LogInformation("[MockLocalization] GetSupportedLanguages");
        IReadOnlyList<SupportedLanguage> languages = new List<SupportedLanguage>
        {
            new() { Id = 1, Code = "en", Name = "English", NativeName = "English", IsDefault = true, IsActive = true, CompletionPercent = 100m },
        };
        return Task.FromResult(languages);
    }

    public Task ImportTranslationsAsync(string languageCode, Dictionary<string, string> translations, CancellationToken ct)
    {
        logger.LogInformation("[MockLocalization] ImportTranslations lang={Lang} count={Count}", languageCode, translations.Count);
        return Task.CompletedTask;
    }

    public Task<Dictionary<string, string>> ExportTranslationsAsync(string languageCode, CancellationToken ct)
    {
        logger.LogInformation("[MockLocalization] ExportTranslations lang={Lang}", languageCode);
        return Task.FromResult(new Dictionary<string, string>());
    }
}
