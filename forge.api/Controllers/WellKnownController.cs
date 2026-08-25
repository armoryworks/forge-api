using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using Forge.Api.Capabilities;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Instance discovery for the mobile app's manual-address path: the phone
/// fetches /.well-known/forge.json from the server the user typed and learns
/// the API base, instance name, allowed auth methods, pinned certificate
/// fingerprint, and minimum app version. Bootstrap-exempt: discovery must
/// answer before anything is configured.
/// </summary>
[ApiController]
[Route(".well-known")]
[CapabilityBootstrap]
public class WellKnownController(
    IOptions<MobileOptions> options,
    ICapabilitySnapshotProvider capabilities) : ControllerBase
{
    public record ForgeWellKnownResponseModel(
        [property: JsonPropertyName("api")] string Api,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("auth")] string[] Auth,
        [property: JsonPropertyName("cert_sha256")] string? CertSha256,
        [property: JsonPropertyName("min_app_version")] string MinAppVersion);

    [HttpGet("forge.json")]
    [AllowAnonymous]
    public ActionResult<ForgeWellKnownResponseModel> Get()
    {
        var auth = new List<string> { "password" };
        if (capabilities.IsEnabled("CAP-IDEN-AUTH-MFA"))
            auth.Add("password+totp");

        var apiBase = $"{Request.Scheme}://{Request.Host}/api";

        return Ok(new ForgeWellKnownResponseModel(
            apiBase,
            options.Value.InstanceName,
            auth.ToArray(),
            string.IsNullOrWhiteSpace(options.Value.CertSha256) ? null : options.Value.CertSha256,
            options.Value.MinAppVersion));
    }
}
