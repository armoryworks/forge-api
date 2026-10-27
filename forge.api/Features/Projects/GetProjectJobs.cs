using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

/// <summary>The jobs linked to the project's WBS elements, by WBS code then job number.</summary>
public record GetProjectJobsQuery(int ProjectId) : IRequest<List<ProjectJobResponseModel>>;

public class GetProjectJobsHandler(AppDbContext db) : IRequestHandler<GetProjectJobsQuery, List<ProjectJobResponseModel>>
{
    public async Task<List<ProjectJobResponseModel>> Handle(GetProjectJobsQuery query, CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == query.ProjectId, cancellationToken))
            throw new KeyNotFoundException($"Project {query.ProjectId} not found");

        return await db.WbsElementJobs.AsNoTracking()
            .Where(l => l.WbsElement.ProjectId == query.ProjectId)
            .OrderBy(l => l.WbsElement.Code).ThenBy(l => l.Job.JobNumber)
            .Select(l => new ProjectJobResponseModel(
                l.JobId,
                l.Job.JobNumber,
                l.Job.Title,
                l.Job.CurrentStage.Name,
                l.Job.CurrentStage.Color,
                l.Job.DueDate,
                l.Job.CompletedDate,
                l.WbsElementId,
                l.WbsElement.Code,
                l.WbsElement.Name))
            .ToListAsync(cancellationToken);
    }
}
