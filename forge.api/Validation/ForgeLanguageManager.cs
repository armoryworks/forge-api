using System.Globalization;

using FluentValidation.Resources;

namespace Forge.Api.Validation;

/// <summary>
/// FluentValidation's English templates reworded to read like the rest of the UI:
/// no quoted field names and no framework phrasing ("must not be empty").
/// Other languages fall through to FluentValidation's own translations.
/// </summary>
public sealed class ForgeLanguageManager : LanguageManager
{
    private static readonly Dictionary<string, string> EnglishOverrides = new(StringComparer.Ordinal)
    {
        ["NotEmptyValidator"] = "{PropertyName} is required.",
        ["NotNullValidator"] = "{PropertyName} is required.",
        ["MaximumLengthValidator"] = "{PropertyName} must be {MaxLength} characters or fewer.",
        ["GreaterThanValidator"] = "{PropertyName} must be greater than {ComparisonValue}.",
        ["GreaterThanOrEqualValidator"] = "{PropertyName} must be at least {ComparisonValue}.",
        ["LessThanValidator"] = "{PropertyName} must be less than {ComparisonValue}.",
        ["LessThanOrEqualValidator"] = "{PropertyName} must be at most {ComparisonValue}.",
        ["EmailValidator"] = "{PropertyName} is not a valid email address.",
    };

    /// <inheritdoc />
    public override string GetString(string key, CultureInfo? culture = null)
    {
        var effective = culture ?? Culture ?? CultureInfo.CurrentUICulture;
        var isEnglish = effective.Equals(CultureInfo.InvariantCulture)
            || effective.TwoLetterISOLanguageName == "en";
        if (Enabled && isEnglish && EnglishOverrides.TryGetValue(key, out var message))
            return message;

        return base.GetString(key, culture);
    }
}
