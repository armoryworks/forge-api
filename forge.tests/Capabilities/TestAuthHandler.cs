using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Phase 4 Phase-A — Test-only authentication handler. Authenticates every
/// request bearing the <c>X-Test-User</c> header.
/// Phase 4 Phase-B — extended to honor <c>X-Test-Role</c> so non-admin paths
/// can be exercised (default: <c>Admin</c>).
/// Absence of <c>X-Test-User</c> = anonymous.
///
/// Lives next to the capability tests because Phase A is the first place
/// the test project needed authenticated endpoint coverage.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var userIdValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Default role is Admin so existing Phase A tests stay green; tests
        // that need to exercise role-restricted branches send X-Test-Role.
        var role = Request.Headers.TryGetValue("X-Test-Role", out var roleValues) && !string.IsNullOrEmpty(roleValues.ToString())
            ? roleValues.ToString()
            : "Admin";

        var userId = string.IsNullOrWhiteSpace(userIdValues.ToString()) ? "1" : "1";

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, "test-user"),
            new Claim(ClaimTypes.Email, "test-user@forge.local"),
            new Claim(ClaimTypes.Role, role),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
