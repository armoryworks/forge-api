namespace Forge.Core.Models;

public record ImportI18nLabelOverridesRequestModel(
    string LanguageCode,
    List<ImportI18nLabelEntryModel> Entries);
