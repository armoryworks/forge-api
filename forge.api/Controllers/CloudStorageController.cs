using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Capabilities.Discovery.Bundles;
using Forge.Api.Features.CloudStorage;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/admin/cloud-storage")]
[Authorize(Roles = "Admin")]
[RequiresCapability("CAP-EXT-CLOUD-STORAGE")]
public class CloudStorageController(IMediator mediator) : ControllerBase
{
    [HttpGet("provider-types")]
    public async Task<ActionResult<List<CloudStorageProviderTypeModel>>> GetProviderTypes(CancellationToken ct)
    {
        var result = await mediator.Send(new GetCloudStorageProviderTypesQuery(), ct);
        return Ok(result);
    }

    [HttpGet("providers")]
    public async Task<ActionResult<List<CloudStorageProviderResponseModel>>> GetProviders(CancellationToken ct)
    {
        var result = await mediator.Send(new GetCloudStorageProvidersQuery(), ct);
        return Ok(result);
    }

    [HttpPost("providers")]
    public async Task<ActionResult<CloudStorageProviderResponseModel>> CreateProvider(
        [FromBody] CreateCloudStorageProviderRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateCloudStorageProviderCommand(model), ct);
        return CreatedAtAction(nameof(GetProviders), new { }, result);
    }

    [HttpPut("providers/{id:int}")]
    public async Task<ActionResult<CloudStorageProviderResponseModel>> UpdateProvider(
        int id, [FromBody] UpdateCloudStorageProviderRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateCloudStorageProviderCommand(id, model), ct);
        return Ok(result);
    }

    [HttpDelete("providers/{id:int}")]
    public async Task<IActionResult> DeleteProvider(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteCloudStorageProviderCommand(id), ct);
        return NoContent();
    }

    [HttpPost("providers/{id:int}/test")]
    public async Task<ActionResult<CloudStorageTestResultModel>> TestProvider(int id, CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await mediator.Send(new TestCloudStorageProviderCommand(id, userId), ct);
        return Ok(result);
    }

    [HttpGet("folder-map")]
    public async Task<ActionResult<List<FolderMapSuggestion>>> GetFolderMap(CancellationToken ct)
    {
        var result = await mediator.Send(new GetCloudStorageFolderMapQuery(), ct);
        return Ok(result);
    }

    [HttpPut("folder-map")]
    public async Task<ActionResult<List<FolderMapSuggestion>>> UpdateFolderMap(
        [FromBody] List<FolderMapSuggestion> entries, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateCloudStorageFolderMapCommand(entries), ct);
        return Ok(result);
    }
}
