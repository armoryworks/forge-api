namespace Forge.Api.Services;

/// <summary>
/// The WebAuthn relying party for one ceremony: the RP ID (the host the browser
/// reached, without port) and the full origins the authenticator's client data
/// may carry. Behind a reverse proxy the request's own scheme and host can lose
/// the port the browser used, so the allowed set also takes the operator's
/// configured public origins (<c>FrontendBaseUrl</c>, <c>Passkeys:Origins</c>) and
/// the browser's <c>Origin</c> header. Every origin must share the RP ID's host;
/// anything else is dropped, so a foreign origin is still refused by the
/// ceremony.
/// </summary>
public sealed record PasskeyRelyingParty(string RpId, IReadOnlySet<string> Origins)
{
    public const string OriginsSettingKey = "Passkeys:Origins";

    public static PasskeyRelyingParty ForOrigin(string origin)
    {
        var uri = new Uri(origin);
        return new PasskeyRelyingParty(uri.Host, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Normalize(uri) });
    }

    public static PasskeyRelyingParty Resolve(IConfiguration config, HttpRequest request)
    {
        var rpId = request.Host.Host;
        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{request.Scheme}://{request.Host}",
        };

        foreach (var candidate in ConfiguredOrigins(config).Append(request.Headers.Origin.ToString()))
        {
            if (TryParseOrigin(candidate, out var uri)
                && string.Equals(uri.Host, rpId, StringComparison.OrdinalIgnoreCase))
            {
                origins.Add(Normalize(uri));
            }
        }

        return new PasskeyRelyingParty(rpId, origins);
    }

    private static IEnumerable<string> ConfiguredOrigins(IConfiguration config)
    {
        var frontend = config[FrontendBaseUrl.SettingKey];
        if (!string.IsNullOrWhiteSpace(frontend)) yield return frontend;

        var section = config.GetSection(OriginsSettingKey);
        if (!string.IsNullOrWhiteSpace(section.Value))
        {
            foreach (var entry in section.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return entry;
        }

        foreach (var child in section.GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Value)) yield return child.Value;
        }
    }

    private static bool TryParseOrigin(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) return false;
        uri = parsed;
        return true;
    }

    private static string Normalize(Uri uri) => uri.GetLeftPart(UriPartial.Authority);
}
