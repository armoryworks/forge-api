using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Forge.Api.Converters;

/// <summary>
/// Accepts both "2026-04-13" (DateOnly) and "2026-04-13T00:00:00Z" (ISO 8601) on read.
/// Always writes "2026-04-13" (standard DateOnly format).
/// </summary>
public class FlexibleDateOnlyConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString() ?? throw new JsonException("Expected a date string.");

        // Try DateOnly format first ("2026-04-13")
        if (DateOnly.TryParseExact(str, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            return dateOnly;

        // Fall back to full ISO 8601 ("2026-04-13T00:00:00Z")
        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return DateOnly.FromDateTime(dt);

        throw new JsonException($"Cannot convert \"{str}\" to DateOnly.");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-dd"));
    }
}
