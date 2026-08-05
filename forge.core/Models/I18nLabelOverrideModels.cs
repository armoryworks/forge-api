namespace Forge.Core.Models;

public record I18nLabelOverrideResponseModel(
    int Id,
    string Key,
    string LanguageCode,
    string Value,
    bool IsMachineTranslated,
    bool IsPendingTranslation,
    string? SourceLanguageCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpsertI18nLabelOverrideRequestModel(
    string Key,
    string LanguageCode,
    string Value,
    bool TranslateToOtherLanguages = true);

public record UpsertI18nLabelOverrideResponseModel(
    List<I18nLabelOverrideResponseModel> Overrides,
    bool TranslationsPending);

public record RetryPendingI18nTranslationsResponseModel(
    int TranslatedCount,
    int StillPendingCount);
