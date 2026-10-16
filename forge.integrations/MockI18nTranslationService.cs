using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;

namespace Forge.Integrations;

public class MockI18nTranslationService(ILogger<MockI18nTranslationService> logger) : II18nTranslationService
{
    public Task<string?> TranslateLabelAsync(string text, string sourceLanguageCode, string targetLanguageCode, CancellationToken ct)
    {
        logger.LogInformation("[MockAI] i18n translation unavailable ({Source} → {Target})", sourceLanguageCode, targetLanguageCode);
        return Task.FromResult<string?>(null);
    }
}
