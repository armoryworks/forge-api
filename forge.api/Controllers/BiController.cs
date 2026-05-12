using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Authentication;
using Forge.Api.Capabilities;

namespace Forge.Api.Controllers;

/// <summary>
/// /api/v1/bi/* — BI-tool integration surface. All endpoints under this
/// route require a valid BI API key issued via /api/v1/admin/bi-api-keys.
///
/// Authentication: <see cref="BiApiKeyAuthenticationOptions.SchemeName"/>.
/// Authorization role: <c>BiApiClient</c> (synthesized by the auth handler).
///
/// Future BI-export endpoints (entity exports, scheduled feeds) belong on
/// this controller (or sibling controllers under the same route prefix and
/// the same <c>[Authorize]</c> stanza).
///
/// Phase 3 / WU-04 / A3.
/// </summary>
[ApiController]
[Route("api/v1/bi")]
[Authorize(
    AuthenticationSchemes = BiApiKeyAuthenticationOptions.SchemeName,
    Roles = "BiApiClient")]
[RequiresCapability("CAP-CROSS-BI-EXPORT")]
public class BiController : ControllerBase
{
    /// <summary>
    /// Probe endpoint — returns identity information about the API key the
    /// caller authenticated with. Useful for BI-tool configuration validation
    /// and harness/smoke testing of the API-key auth scheme.
    /// </summary>
    [HttpGet("whoami")]
    public ActionResult<object> WhoAmI()
    {
        var keyId = User.FindFirstValue("bi_api_key_id");
        var keyPrefix = User.FindFirstValue("bi_api_key_prefix");
        var name = User.Identity?.Name;
        return Ok(new
        {
            scheme = BiApiKeyAuthenticationOptions.SchemeName,
            keyId,
            keyPrefix,
            name,
            roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
        });
    }
}
