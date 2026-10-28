using FluentValidation;
using FluentValidation.Results;
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
///
/// Per-column filters are checked against <see cref="JobListFields"/> and the
/// caller's <see cref="JobListFieldAccess"/>: a malformed filter, or one on a
/// field the caller may not read, is a 400.
/// </summary>
public record GetJobsQuery(JobListQuery Query) : IRequest<PagedResponse<JobListResponseModel>>;

public class GetJobsHandler(IJobRepository repo, IJobOperationService operations, IHttpContextAccessor httpContext)
    : IRequestHandler<GetJobsQuery, PagedResponse<JobListResponseModel>>
{
    public async Task<PagedResponse<JobListResponseModel>> Handle(
        GetJobsQuery request, CancellationToken cancellationToken)
    {
        var access = JobListFieldAccessResolver.For(httpContext.HttpContext?.User);
        EnsureFiltersAllowed(request.Query, access);

        var tracking = await operations.IsTrackingEnabledAsync(cancellationToken);
        var page = await repo.GetPagedJobsAsync(request.Query, tracking, access, cancellationToken);
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

    private static void EnsureFiltersAllowed(JobListQuery query, JobListFieldAccess access)
    {
        var failures = new List<ValidationFailure>();
        foreach (var raw in query.Filter ?? [])
        {
            var filter = JobListFilter.Parse(raw, out var error);
            if (filter is null)
                failures.Add(new ValidationFailure(nameof(JobListQuery.Filter), error));
            else if (!access.Allows(filter.Field.Gate))
                failures.Add(new ValidationFailure(nameof(JobListQuery.Filter), $"'{filter.Field.Key}' is not a filterable job field."));
        }
        if (failures.Count > 0)
            throw new ValidationException(failures);
    }
}
