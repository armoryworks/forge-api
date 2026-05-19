namespace Forge.Api.Features.Auth;

/// <summary>
/// Raised by <see cref="SsoCallbackHandler"/> when the external account's
/// email domain is not in the configured <c>SsoOptions.{Provider}.AllowedDomains</c>
/// list. Mapped to HTTP 403 by <c>ExceptionHandlingMiddleware</c>; the
/// browser-flow callback catches it and redirects with
/// <c>?error=domain_not_permitted</c>.
/// </summary>
public class SsoDomainNotPermittedException : Exception
{
    public string Provider { get; }
    public string EmailDomain { get; }

    public SsoDomainNotPermittedException(string provider, string emailDomain)
        : base($"Email domain '{emailDomain}' is not permitted for the '{provider}' SSO provider.")
    {
        Provider = provider;
        EmailDomain = emailDomain;
    }
}
