using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Core.Interfaces;
using Forge.Api.Capabilities;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/admin/user-integrations")]
[Authorize(Roles = "Admin")]
// admin view/revoke of the per-user credential store (Email/DocuSeal/QuickBooks/Shipping/Webhook/Sms) — the features that USE each credential carry their own EXT gate; the store itself is infrastructure
[CapabilityBootstrap]
public class AdminUserIntegrationsController(IUserIntegrationService integrationService) : ControllerBase
{
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    /// <summary>
    /// Get integration summaries for a specific user (no credentials).
    /// </summary>
    [HttpGet("{userId:int}")]
    public async Task<IActionResult> GetUserIntegrations(int userId, CancellationToken ct)
    {
        var result = await integrationService.AdminGetUserIntegrationsAsync(userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Revoke a user's integration with full audit trail.
    /// </summary>
    [HttpDelete("{userId:int}/{integrationId:int}")]
    public async Task<IActionResult> RevokeIntegration(
        int userId, int integrationId, [FromBody] AdminRevokeRequest? request, CancellationToken ct)
    {
        await integrationService.AdminRevokeAsync(GetUserId(), userId, integrationId, request?.Reason, ct);
        return NoContent();
    }
}

public record AdminRevokeRequest(string? Reason);
