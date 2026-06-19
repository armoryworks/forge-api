using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.Shipments;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Inbound carrier tracking webhook. Anonymous (a carrier has no Forge session) and guarded instead by a
/// shared secret: when <c>Shipping:WebhookSecret</c> is configured, the X-Forge-Shipping-Secret header must
/// match. Best-effort — always returns 200 quickly so the carrier doesn't retry-storm; the ingest handler
/// decides whether anything changed. A provider-specific payload (e.g. EasyPost) is mapped to the normalized
/// <see cref="TrackingWebhookRequestModel"/> before it reaches here.
/// </summary>
[ApiController]
[Route("api/v1/shipping")]
public class ShippingWebhookController(
    IMediator mediator,
    IConfiguration configuration,
    ILogger<ShippingWebhookController> logger) : ControllerBase
{
    [HttpPost("tracking-webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> TrackingWebhook(
        [FromBody] TrackingWebhookRequestModel request,
        [FromHeader(Name = "X-Forge-Shipping-Secret")] string? secret)
    {
        var expected = configuration["Shipping:WebhookSecret"];
        if (!string.IsNullOrEmpty(expected) && !string.Equals(expected, secret, StringComparison.Ordinal))
        {
            logger.LogWarning("Tracking webhook rejected: bad or missing shared secret");
            return Unauthorized();
        }

        var marked = await mediator.Send(new IngestTrackingUpdateCommand(request.TrackingNumber, request.Status));
        return Ok(new { marked });
    }
}
