using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

public record EmployeeJobItem(
    int Id,
    string JobNumber,
    string Title,
    string? StageName,
    string? StageColor,
    string? TrackTypeName,
    string Priority,
    DateTimeOffset? DueDate,
    DateTimeOffset CreatedAt);

public record GetEmployeeJobsQuery(int EmployeeId) : IRequest<List<EmployeeJobItem>>;

public class GetEmployeeJobsHandler(AppDbContext db)
    : IRequestHandler<GetEmployeeJobsQuery, List<EmployeeJobItem>>
{
    public async Task<List<EmployeeJobItem>> Handle(GetEmployeeJobsQuery request, CancellationToken cancellationToken)
    {
        return await db.Jobs
            .Include(j => j.CurrentStage)
            .Include(j => j.TrackType)
            .AsNoTracking()
            .Where(j => j.AssigneeId == request.EmployeeId && !j.IsArchived && j.DeletedAt == null)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new EmployeeJobItem(
                j.Id,
                j.JobNumber,
                j.Title,
                j.CurrentStage.Name,
                j.CurrentStage.Color,
                j.TrackType.Name,
                j.Priority.ToString(),
                j.DueDate,
                j.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
