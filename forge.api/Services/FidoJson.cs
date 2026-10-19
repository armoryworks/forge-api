using System.Text.Json;

namespace Forge.Api.Services;

/// <summary>
/// Reads WebAuthn authenticator responses with default serializer options, so
/// Fido2's own type-level converters apply instead of the app-wide MVC JSON
/// options (whose string-enum converter rejects <c>public-key</c>).
/// </summary>
public static class FidoJson
{
    private static readonly JsonSerializerOptions Options = new();

    /// <summary>The deserialized payload, or null when it is missing or malformed.</summary>
    public static T? Read<T>(JsonElement element) where T : class
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        try
        {
            return element.Deserialize<T>(Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
