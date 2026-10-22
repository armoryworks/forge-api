using FluentValidation.Results;

namespace Forge.Api.Validation;

/// <summary>
/// The rule arguments a client needs to word a validation error in its own language,
/// taken from FluentValidation's message placeholders. Only configured limits are passed
/// (lengths, comparison values, ranges, precision); the submitted value never is.
/// </summary>
public static class ValidationErrorParams
{
    private static readonly string[] AllowedPlaceholders =
    [
        "MaxLength", "MinLength", "ComparisonValue", "From", "To", "ExpectedPrecision", "ExpectedScale",
    ];

    /// <summary>
    /// The allowed placeholder values of <paramref name="failure"/>, keyed in camelCase,
    /// or null when it carries none.
    /// </summary>
    public static Dictionary<string, object?>? From(ValidationFailure failure)
    {
        var values = failure.FormattedMessagePlaceholderValues;
        if (values is null || values.Count == 0) return null;

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in AllowedPlaceholders)
        {
            if (values.TryGetValue(name, out var value))
                result[char.ToLowerInvariant(name[0]) + name[1..]] = value;
        }
        return result.Count == 0 ? null : result;
    }
}
