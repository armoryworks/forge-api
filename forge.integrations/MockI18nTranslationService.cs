using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockI18nTranslationService(ILogger<MockI18nTranslationService> logger) : II18nTranslationService
{
    public Task<I18nTranslationResult> TranslateLabelAsync(string text, string sourceLanguageCode, string targetLanguageCode, CancellationToken ct)
    {
        logger.LogInformation("[MockAI] i18n translation unavailable ({Source} → {Target})", sourceLanguageCode, targetLanguageCode);
        return Task.FromResult(I18nTranslationResult.Unavailable);
    }
}
