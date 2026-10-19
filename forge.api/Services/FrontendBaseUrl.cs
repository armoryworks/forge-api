namespace Forge.Api.Services;

/// <summary>
/// The public base URL that links in outbound email point at. It comes from the
/// operator's <c>FrontendBaseUrl</c> setting, never from the caller's
/// <c>Origin</c> or forwarded-host headers: an anonymous caller could otherwise
/// have a real one-time sign-in token mailed inside a link to a host they
/// control. The request's own scheme and host are used only when the setting
/// is blank.
/// </summary>
public static class FrontendBaseUrl
{
    public const string SettingKey = "FrontendBaseUrl";

    public static string Resolve(IConfiguration config, HttpRequest request)
    {
        var configured = config[SettingKey];
        return string.IsNullOrWhiteSpace(configured)
            ? $"{request.Scheme}://{request.Host}"
            : configured.Trim().TrimEnd('/');
    }
}
