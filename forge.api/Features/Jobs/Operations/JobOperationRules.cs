using MediatR;
using Microsoft.AspNetCore.SignalR;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs.Operations;

public static class JobOperationRules
{
    public const string TrackingOffMessage = "Operation tracking is turned off (Admin → Settings → Shop Floor).";
    public const string ReopenFirstMessage = "Reopen the operation first.";
    public const string StaleMessage = "This operation was changed by someone else — refresh and try again.";

    public static bool IsClosed(JobOperationStatus status)
        => status is JobOperationStatus.Complete or JobOperationStatus.Skipped;

    public static string Describe(JobOperation row, decimal jobQuantity)
    {
        var text = $"{row.Status} {row.CompletedQuantity:0.##}/{jobQuantity:0.##}";
        return row.ScrapQuantity > 0m ? $"{text} ({row.ScrapQuantity:0.##} scrap)" : text;
    }

    public static async Task BroadcastJobUpdatedAsync(
        IHubContext<BoardHub> boardHub, IMediator mediator, Job job, CancellationToken ct)
    {
        var detail = await mediator.Send(new GetJobByIdQuery(job.Id), ct);
        var evt = new BoardJobUpdatedEvent(job.Id, detail);
        await boardHub.Clients.Group($"board:{job.TrackTypeId}").SendAsync("jobUpdated", evt, ct);
        await boardHub.Clients.Group($"job:{job.Id}").SendAsync("jobUpdated", evt, ct);
    }
}
