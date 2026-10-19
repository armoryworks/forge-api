using MediatR;

using Forge.Api.Features.Notifications;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Jobs;

public class CheckApprovalEscalationsJob(
    IApprovalService approvalService,
    IMediator mediator,
    ILogger<CheckApprovalEscalationsJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var escalated = await approvalService.CheckEscalationsAsync(ct);
        var notified = 0;

        foreach (var request in escalated)
        {
            var approverIds = await approvalService.GetCurrentApproverIdsAsync(request.Id, ct);
            if (approverIds.Count == 0)
            {
                logger.LogWarning(
                    "Approval request {RequestId} escalated to step {Step}, which has no active approver to notify",
                    request.Id, request.CurrentStepNumber);
                continue;
            }

            var subject = string.IsNullOrWhiteSpace(request.EntitySummary)
                ? $"{request.EntityType} #{request.EntityId}"
                : request.EntitySummary;

            foreach (var approverId in approverIds)
            {
                await mediator.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                    approverId,
                    "approval_escalated",
                    "warning",
                    "approvals",
                    "Approval escalated to you",
                    $"{subject} was not decided in time and now waits for your approval.",
                    request.EntityType,
                    request.EntityId,
                    SenderId: null)), ct);
                notified++;
            }
        }

        if (escalated.Count > 0)
            logger.LogInformation("Escalated {Count} approval request(s), sent {Notified} notification(s)",
                escalated.Count, notified);
    }
}
