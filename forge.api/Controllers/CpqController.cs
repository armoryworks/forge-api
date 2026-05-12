using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Cpq;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/cpq")]
[Authorize]
[RequiresCapability("CAP-O2C-CPQ")]
public class CpqController(IMediator mediator) : ControllerBase
{
    [HttpGet("configurators")]
    public async Task<ActionResult<List<ConfiguratorResponseModel>>> GetConfigurators(
        [FromQuery] bool? isActive = null,
        [FromQuery] int? basePartId = null)
    {
        var result = await mediator.Send(new GetConfiguratorsQuery(isActive, basePartId));
        return Ok(result);
    }

    [HttpPost("configurators")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<ConfiguratorResponseModel>> CreateConfigurator([FromBody] CreateConfiguratorRequestModel request)
    {
        var result = await mediator.Send(new CreateConfiguratorCommand(request));
        return CreatedAtAction(nameof(GetConfiguratorById), new { id = result.Id }, result);
    }

    [HttpGet("configurators/{id:int}")]
    public async Task<ActionResult<ConfiguratorDetailResponseModel>> GetConfiguratorById(int id)
    {
        var result = await mediator.Send(new GetConfiguratorByIdQuery(id));
        return Ok(result);
    }

    [HttpPut("configurators/{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> UpdateConfigurator(int id, [FromBody] UpdateConfiguratorRequestModel request)
    {
        await mediator.Send(new UpdateConfiguratorCommand(id, request));
        return NoContent();
    }

    [HttpPost("configure")]
    public async Task<ActionResult<CpqResult>> ConfigureProduct([FromBody] ConfigureProductRequestModel request)
    {
        var result = await mediator.Send(new ConfigureProductCommand(request));
        return Ok(result);
    }

    [HttpPost("validate")]
    public async Task<ActionResult<CpqValidationResponseModel>> ValidateSelections([FromBody] ConfigureProductRequestModel request)
    {
        var result = await mediator.Send(new ValidateSelectionsQuery(request));
        return Ok(result);
    }

    [HttpPost("configurations")]
    public async Task<ActionResult<ProductConfigurationResponseModel>> SaveConfiguration([FromBody] SaveConfigurationRequestModel request)
    {
        var result = await mediator.Send(new SaveConfigurationCommand(request));
        return CreatedAtAction(nameof(GetConfiguration), new { id = result.Id }, result);
    }

    [HttpGet("configurations/{id:int}")]
    public async Task<ActionResult<ProductConfigurationResponseModel>> GetConfiguration(int id)
    {
        var result = await mediator.Send(new GetConfigurationByIdQuery(id));
        return Ok(result);
    }

    [HttpPost("configurations/{id:int}/generate-quote")]
    public async Task<ActionResult<object>> GenerateQuote(int id, [FromBody] GenerateQuoteFromConfigRequestModel request)
    {
        var quoteId = await mediator.Send(new GenerateQuoteFromConfigCommand(id, request));
        return Ok(new { quoteId });
    }

    [HttpPost("configurations/{id:int}/generate-part")]
    public async Task<ActionResult<object>> GeneratePart(int id)
    {
        var partId = await mediator.Send(new GeneratePartFromConfigCommand(id));
        return Ok(new { partId });
    }
}
