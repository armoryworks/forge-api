using MediatR;

using Forge.Api.Features.DomainEvents;
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
        decimal? amount, string? entitySummary, CancellationToken ct)
    {
        var request = await approvalService.SubmitForApprovalAsync(
            entityType, entityId, requestedById, amount, entitySummary, ct);

        if (request?.Status == ApprovalRequestStatus.AutoApproved)
        {
            await publisher.Publish(
                new ApprovalCompletedEvent(entityType, entityId, Approved: true, requestedById, AutoApprovedNote),
                ct);
        }

        return request;
    }
}
