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

public class GetJobsHandler(IJobRepository repo)
    : IRequestHandler<GetJobsQuery, PagedResponse<JobListResponseModel>>
{
    public Task<PagedResponse<JobListResponseModel>> Handle(
        GetJobsQuery request, CancellationToken cancellationToken)
        => repo.GetPagedJobsAsync(request.Query, cancellationToken);
}
