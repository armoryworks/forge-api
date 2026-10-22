using Forge.Core.Enums;

namespace Forge.Core.Models;

public readonly record struct I18nTranslationResult(I18nTranslationOutcome Outcome, string? Value)
{
    public static I18nTranslationResult Unavailable => new(I18nTranslationOutcome.Unavailable, null);

    public static I18nTranslationResult Rejected => new(I18nTranslationOutcome.Rejected, null);

    public static I18nTranslationResult Translated(string value) => new(I18nTranslationOutcome.Translated, value);
}
