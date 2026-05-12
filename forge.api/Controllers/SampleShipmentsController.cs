using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.SampleShipments;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/sample-shipments")]
[Authorize(Roles = "Admin,Manager,PM,Engineer")]
[RequiresCapability("CAP-O2C-LEAD")]
public class SampleShipmentsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SampleShipmentResponseModel>>> Get([FromQuery] int? leadId)
        => Ok(await mediator.Send(new GetSampleShipmentsQuery(leadId)));

    [HttpPost]
    public async Task<ActionResult<SampleShipmentResponseModel>> Create([FromBody] CreateSampleShipmentRequest request)
    {
        var result = await mediator.Send(new CreateSampleShipmentCommand(request));
        return Created($"/api/v1/sample-shipments/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SampleShipmentResponseModel>> Update(int id, [FromBody] UpdateSampleShipmentRequest request)
        => Ok(await mediator.Send(new UpdateSampleShipmentCommand(id, request)));
}
