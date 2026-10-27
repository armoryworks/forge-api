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

    [HttpGet("event-types")]
    public ActionResult<List<string>> GetEventTypes()
    {
        return Ok(WebhookEventTypes.All.Order(StringComparer.Ordinal).ToList());
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

    [HttpPut("{subscriptionId:int}")]
    public async Task<ActionResult<WebhookSubscriptionResponseModel>> UpdateSubscription(
        int subscriptionId, [FromBody] UpdateWebhookSubscriptionRequestModel request)
    {
        var result = await mediator.Send(new UpdateWebhookSubscriptionCommand(
            subscriptionId,
            request.Url,
            request.EventTypesJson,
            request.Description,
            request.HeadersJson,
            request.MaxRetries,
            request.AutoDisableOnFailure,
            request.IsActive,
            request.RotateSecret));
        return Ok(result);
    }

    [HttpDelete("{subscriptionId:int}")]
    public async Task<IActionResult> DeleteSubscription(int subscriptionId)
    {
        await mediator.Send(new DeleteWebhookSubscriptionCommand(subscriptionId));
        return NoContent();
    }

    [HttpPost("{subscriptionId:int}/test")]
    public async Task<ActionResult<WebhookDeliveryResponseModel>> TestSubscription(int subscriptionId)
    {
        var result = await mediator.Send(new TestWebhookSubscriptionCommand(subscriptionId));
        return Ok(result);
    }

    [HttpGet("{subscriptionId:int}/deliveries")]
    public async Task<ActionResult<List<WebhookDeliveryResponseModel>>> GetDeliveries(int subscriptionId)
    {
        var result = await mediator.Send(new GetWebhookDeliveriesQuery(subscriptionId));
        return Ok(result);
    }

    [HttpPost("deliveries/{deliveryId:int}/redeliver")]
    public async Task<ActionResult<WebhookDeliveryResponseModel>> Redeliver(int deliveryId)
    {
        var result = await mediator.Send(new RedeliverWebhookDeliveryCommand(deliveryId));
        return Ok(result);
    }
}
