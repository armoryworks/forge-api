using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Webhooks;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/admin/webhooks")]
[Authorize(Roles = "Admin")]
[RequiresCapability("CAP-CROSS-WEBHOOKS")]
public class WebhooksController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<WebhookSubscriptionResponseModel>>> GetSubscriptions()
    {
        var result = await mediator.Send(new GetWebhookSubscriptionsQuery());
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<WebhookSubscriptionResponseModel>> CreateSubscription([FromBody] CreateWebhookSubscriptionRequestModel request)
    {
        var result = await mediator.Send(new CreateWebhookSubscriptionCommand(
            request.Url,
            request.EventTypesJson,
            request.Secret,
            request.Description,
            request.HeadersJson,
            request.MaxRetries,
            request.AutoDisableOnFailure));

        return CreatedAtAction(nameof(GetSubscriptions), new { }, result);
    }

    [HttpGet("{subscriptionId:int}/deliveries")]
    public async Task<ActionResult<List<WebhookDeliveryResponseModel>>> GetDeliveries(int subscriptionId)
    {
        var result = await mediator.Send(new GetWebhookDeliveriesQuery(subscriptionId));
        return Ok(result);
    }
}
