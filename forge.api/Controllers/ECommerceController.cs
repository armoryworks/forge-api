using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.ECommerce;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/admin/ecommerce")]
[Authorize(Roles = "Admin,Manager")]
public class ECommerceController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ECommerceIntegrationResponseModel>>> GetIntegrations()
    {
        var result = await mediator.Send(new GetECommerceIntegrationsQuery());
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ECommerceIntegrationResponseModel>> CreateIntegration(
        [FromBody] CreateECommerceIntegrationRequestModel model)
    {
        var result = await mediator.Send(new CreateECommerceIntegrationCommand(model));
        return CreatedAtAction(nameof(GetIntegrations), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ECommerceIntegrationResponseModel>> UpdateIntegration(
        int id, [FromBody] UpdateECommerceIntegrationRequestModel model)
    {
        var result = await mediator.Send(new UpdateECommerceIntegrationCommand(id, model));
        return Ok(result);
    }

    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<TestECommerceConnectionResult>> TestConnection(int id)
    {
        var result = await mediator.Send(new TestECommerceConnectionCommand(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/import")]
    public async Task<ActionResult<List<ECommerceOrderSyncResponseModel>>> ImportOrders(int id)
    {
        var result = await mediator.Send(new ImportECommerceOrdersCommand(id));
        return Ok(result);
    }

    [HttpGet("{id:int}/syncs")]
    public async Task<ActionResult<List<ECommerceOrderSyncResponseModel>>> GetOrderSyncs(int id)
    {
        var result = await mediator.Send(new GetECommerceOrderSyncsQuery(id));
        return Ok(result);
    }

    [HttpPost("syncs/{syncId:int}/retry")]
    public async Task<ActionResult<ECommerceOrderSyncResponseModel>> RetryImport(int syncId)
    {
        var result = await mediator.Send(new RetryECommerceImportCommand(syncId));
        return Ok(result);
    }
}
