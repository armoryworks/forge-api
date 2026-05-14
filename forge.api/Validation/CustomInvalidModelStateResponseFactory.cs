using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Forge.Api.Validation;

/// <summary>
/// Replaces ASP.NET Core's default model-binding error response (which leaks
/// framework jargon like "The JSON value could not be converted to
/// System.DateTime. Path: $.someField | LineNumber: 0 | BytePositionInLine: 42")
/// with a clean structured envelope:
///
///   { "errors": [ { "field": "someField", "message": "Date is not valid", "rejectedValue": "2025-02-29" } ] }
///
/// Per-controller validation behavior is unchanged — only the SHAPE of the
/// binding-layer response is standardized. HTTP 400 is preserved.
///
/// Cases addressed (Phase 3 H6): EDGE-DATE-LEAP-001/003/004, EDGE-DATE-DST-001..004,
/// EDGE-DATE-FYBOUNDARY-001..004, EDGE-DATE-TZBOUNDARY-001, EDGE-DATE-TZSHIFT-001..003,
/// EDGE-DECIMAL-PRECISION-001..004, plus any other case asserting friendly error text.
/// </summary>
public static class CustomInvalidModelStateResponseFactory
{
    /// <summary>
    /// Build the response factory delegate to plug into ApiBehaviorOptions.
    /// </summary>
    public static Func<ActionContext, IActionResult> Create()
    {
        return context =>
        {
            var errors = context.ModelState
                .Where(kvp => kvp.Value?.Errors.Count > 0)
                .Select(kvp => new
                {
                    field = NormalizeFieldName(kvp.Key),
                    message = HumanizeError(kvp.Value!.Errors[0]),
                    rejectedValue = kvp.Value!.AttemptedValue
                })
                .ToArray();

            return new BadRequestObjectResult(new { errors });
        };
    }

    /// <summary>
    /// Strip the "$." JSON-path prefix and any trailing "LineNumber|BytePositionInLine"
    /// debugging segments from a model-state key. Convert the leading character to
    /// camelCase if it is uppercase (e.g. "$.SomeField" -> "someField").
    /// </summary>
    internal static string NormalizeFieldName(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw ?? string.Empty;

        var name = raw;

        // Strip leading "$." — the System.Text.Json path prefix.
        if (name.StartsWith("$.", StringComparison.Ordinal))
            name = name[2..];
        else if (name.StartsWith("$", StringComparison.Ordinal))
            name = name[1..];

        // Strip any "| LineNumber: ... | BytePositionInLine: ..." debug suffix.
        var pipeIdx = name.IndexOf('|');
        if (pipeIdx >= 0)
            name = name[..pipeIdx].TrimEnd();

        // Drop empty segments that can occur after stripping "$.".
        name = name.Trim();

        if (name.Length == 0)
            return name;

        // CamelCase the leading segment if it's PascalCase (only the first char,
        // and only when followed by a lowercase char — otherwise leave acronyms alone).
        if (char.IsUpper(name[0]) && (name.Length == 1 || char.IsLower(name[1])))
        {
            name = char.ToLowerInvariant(name[0]) + name[1..];
        }

        return name;
    }

    /// <summary>
    /// Replace common framework binding-error text with friendlier equivalents.
    /// Unknown messages pass through unchanged so we do not mask information.
    /// </summary>
    internal static string HumanizeError(ModelError err)
    {
        if (err is null)
            return string.Empty;

        // Walk the exception chain. System.Text.Json wraps inner converter
        // failures under outer "could not be converted to <RecordType>" messages
        // — the inner JsonException carries the actual primitive ("System.DateTime",
        // "System.Int32", etc.). Pick the deepest message, then fall back to the
        // outer ErrorMessage when there's no exception.
        var raw = err.ErrorMessage;
        if (err.Exception is not null)
        {
            var innermost = err.Exception;
            while (innermost.InnerException is not null)
                innermost = innermost.InnerException;
            // Prefer the inner message if it names a primitive type — otherwise
            // keep ErrorMessage so we don't lose the framework's chosen wording.
            var innerMsg = innermost.Message ?? string.Empty;
            if (innerMsg.Contains("could not be converted to System.", StringComparison.Ordinal))
                raw = innerMsg;
            else if (string.IsNullOrWhiteSpace(raw))
                raw = innerMsg;
        }

        if (string.IsNullOrWhiteSpace(raw))
            return "Invalid value";

        // Strip any "Path: $.xxx | LineNumber: 0 | BytePositionInLine: 42" trailing
        // diagnostic chunk before pattern-matching. The path is already captured
        // separately as the field name, and line/byte positions are noise to humans.
        var cleaned = StripJsonPathSuffix(raw);

        // Conversion-failure family — System.Text.Json emits these when a primitive
        // type cannot be deserialized from the supplied JSON value.
        if (cleaned.Contains("could not be converted to System.DateTime", StringComparison.Ordinal)
            || cleaned.Contains("could not be converted to System.DateTimeOffset", StringComparison.Ordinal)
            || cleaned.Contains("could not be converted to System.DateOnly", StringComparison.Ordinal)
            || cleaned.Contains("could not be converted to System.TimeOnly", StringComparison.Ordinal)
            || cleaned.Contains("could not be converted to System.TimeSpan", StringComparison.Ordinal))
        {
            return "Date is not valid";
        }

        if (cleaned.Contains("could not be converted to System.Decimal", StringComparison.Ordinal)
            || cleaned.Contains("could not be converted to System.Double", StringComparison.Ordinal)
            || cleaned.Contains("could not be converted to System.Single", StringComparison.Ordinal))
        {
            return "Number is not valid";
        }

        if (cleaned.Contains("could not be converted to System.Int32", StringComparison.Ordinal)
            || cleaned.Contains("could not be converted to System.Int64", StringComparison.Ordinal)
            || cleaned.Contains("could not be converted to System.Int16", StringComparison.Ordinal)
            || cleaned.Contains("could not be converted to System.Byte", StringComparison.Ordinal))
        {
            return "Whole number is required";
        }

        if (cleaned.Contains("could not be converted to System.Boolean", StringComparison.Ordinal))
        {
            return "True or false expected";
        }

        if (cleaned.Contains("could not be converted to System.Guid", StringComparison.Ordinal))
        {
            return "Identifier is not valid";
        }

        // Enum-binding failures — System.Text.Json's JsonStringEnumConverter throws
        // a generic "could not be converted to <Namespace>.<EnumType>" when the
        // supplied string is not a member. The outer wrapper ("could not be
        // converted to <RequestModel>") is unhelpful too; the inner exception's
        // first line is the same shape but on the actual enum type. (Phase 3 F6.)
        if (cleaned.Contains("could not be converted to Forge.Platform.Enums.", StringComparison.Ordinal))
        {
            return "Value is not one of the allowed values";
        }

        // The outer model-record conversion failure is what the user sees when
        // any nested field (most commonly an enum) fails to convert. Without a
        // dedicated message it leaks "Forge.Core.Models.<RequestModel>" to
        // users. (Phase 3 F6.)
        if (cleaned.Contains("could not be converted to Forge.Core.Models.", StringComparison.Ordinal))
        {
            return "One or more fields contain an invalid value";
        }

        // Required-body family
        if (cleaned.Contains("non-empty request body is required", StringComparison.OrdinalIgnoreCase))
        {
            return "Request body is required";
        }

        // Required-field family — model binding emits "The X field is required."
        // Keep as-is — it's already user-friendly.
        // Fallthrough: keep the cleaned framework message rather than masking info.
        return cleaned;
    }

    private static readonly Regex JsonPathSuffixRegex = new(
        @"\s*Path:\s*\$[^|]*(\|\s*LineNumber:\s*\d+)?(\s*\|\s*BytePositionInLine:\s*\d+)?\.?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string StripJsonPathSuffix(string raw)
    {
        var stripped = JsonPathSuffixRegex.Replace(raw, string.Empty).TrimEnd();
        // Drop a trailing period that the framework leaves dangling.
        if (stripped.EndsWith('.'))
            stripped = stripped[..^1];
        return stripped;
    }
}
