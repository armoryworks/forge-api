using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs;

/// <summary>
/// Phase 3 F7-broad / WU-22 — paged job-list query.
///
/// Replaces the previous (trackTypeId, stageId, assigneeId, isArchived,
/// search, customerId) signature with the bound JobListQuery model. The
/// controller continues to accept the legacy query-param names so existing
/// callers work unchanged. Specialised list endpoints (kanban, calendar)
/// remain on the legacy unpaged path.
/// </summary>
public record GetJobsQuery(JobListQuery Query) : IRequest<PagedResponse<JobListResponseModel>>;

public class GetJobsHandler(IJobRepository repo, IJobOperationService operations)
    : IRequestHandler<GetJobsQuery, PagedResponse<JobListResponseModel>>
{
    public async Task<PagedResponse<JobListResponseModel>> Handle(
        GetJobsQuery request, CancellationToken cancellationToken)
    {
        var tracking = await operations.IsTrackingEnabledAsync(cancellationToken);
        var page = await repo.GetPagedJobsAsync(request.Query, tracking, cancellationToken);
        if (page.Items.Count == 0 || !tracking)
            return page;

        var summaries = await operations.SummarizeAsync(page.Items.Select(j => j.Id).ToList(), cancellationToken);
        if (summaries.Count == 0)
            return page;

        var items = page.Items
            .Select(item => summaries.TryGetValue(item.Id, out var summary)
                ? item with
                {
                    OperationsTotal = summary.OperationsTotal,
                    OperationsComplete = summary.OperationsComplete,
                    InProgressSteps = summary.InProgressSteps,
                    RunningTimerCount = summary.RunningTimerCount,
                    EstimatedRemainingMinutes = summary.EstimatedRemainingMinutes,
                }
                : item)
            .ToList();

        return page with { Items = items };
    }
}
