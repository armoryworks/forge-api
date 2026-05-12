using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Core.Interfaces;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/user-integrations")]
[Authorize]
public class UserIntegrationsController(IUserIntegrationService integrationService) : ControllerBase
{
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetMyIntegrations(CancellationToken ct)
    {
        var result = await integrationService.GetUserIntegrationsAsync(GetUserId(), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetIntegration(int id, CancellationToken ct)
    {
        var integration = await integrationService.GetByIdAsync(GetUserId(), id, ct);
        if (integration is null) return NotFound();

        // Return summary only — never expose EncryptedCredentials
        return Ok(new
        {
            integration.Id,
            integration.Category,
            integration.ProviderId,
            integration.DisplayName,
            integration.IsActive,
            integration.LastSyncAt,
            integration.LastError,
            integration.ConfigJson,
            integration.CreatedAt,
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateIntegration([FromBody] CreateIntegrationRequest request, CancellationToken ct)
    {
        var integration = await integrationService.CreateAsync(
            GetUserId(), request.Category, request.ProviderId,
            request.DisplayName, request.CredentialsJson, request.ConfigJson, ct);

        return CreatedAtAction(nameof(GetIntegration), new { id = integration.Id }, new
        {
            integration.Id,
            integration.Category,
            integration.ProviderId,
            integration.DisplayName,
            integration.IsActive,
            integration.CreatedAt,
        });
    }

    [HttpPut("{id:int}/credentials")]
    public async Task<IActionResult> UpdateCredentials(int id, [FromBody] UpdateCredentialsRequest request, CancellationToken ct)
    {
        await integrationService.UpdateCredentialsAsync(GetUserId(), id, request.CredentialsJson, ct);
        return NoContent();
    }

    [HttpPut("{id:int}/config")]
    public async Task<IActionResult> UpdateConfig(int id, [FromBody] UpdateConfigRequest request, CancellationToken ct)
    {
        await integrationService.UpdateConfigAsync(GetUserId(), id, request.ConfigJson, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Disconnect(int id, CancellationToken ct)
    {
        await integrationService.DisconnectAsync(GetUserId(), id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/test")]
    public async Task<IActionResult> TestConnection(int id, CancellationToken ct)
    {
        var success = await integrationService.TestConnectionAsync(GetUserId(), id, ct);
        return Ok(new { success });
    }

    [HttpGet("providers")]
    public IActionResult GetAvailableProviders()
    {
        var providers = integrationService.GetAvailableProviders();
        return Ok(providers);
    }
}

// Request models (small, co-located per convention)
public record CreateIntegrationRequest(
    string Category,
    string ProviderId,
    string? DisplayName,
    string CredentialsJson,
    string? ConfigJson = null);

public record UpdateCredentialsRequest(string CredentialsJson);

public record UpdateConfigRequest(string? ConfigJson);
