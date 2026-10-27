using MediatR;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Approvals;

public static class ApprovalSubmission
{
    public const string AutoApprovedNote = "Auto-approved (below threshold)";

    public static async Task<ApprovalRequest?> SubmitAndPublishAsync(
        this IApprovalService approvalService, IPublisher publisher,
        string entityType, int entityId, int requestedById,
        decimal? amount, string? entitySummary, CancellationToken ct, IAssignmentNotifier? notifier = null)
    {
        var request = await approvalService.SubmitForApprovalAsync(
            entityType, entityId, requestedById, amount, entitySummary, ct);

        await approvalService.NotifyApproversAsync(notifier, request, ct);

        if (request?.Status == ApprovalRequestStatus.AutoApproved)
        {
            await publisher.Publish(
                new ApprovalCompletedEvent(entityType, entityId, Approved: true, requestedById, AutoApprovedNote),
                ct);
        }

        return request;
    }

    public static async Task NotifyApproversAsync(
        this IApprovalService approvalService, IAssignmentNotifier? notifier, ApprovalRequest? request, CancellationToken ct)
    {
        if (notifier is null || request is not { Status: ApprovalRequestStatus.Pending or ApprovalRequestStatus.Escalated })
            return;

        var approverIds = await approvalService.GetCurrentApproverIdsAsync(request.Id, ct);
        await notifier.ApprovalAssignedAsync(request.EntityType, request.EntityId, request.EntitySummary, approverIds, ct);
    }
}
