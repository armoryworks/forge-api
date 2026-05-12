using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

public record EmployeeTrainingItem(
    int Id,
    string ModuleName,
    string ModuleType,
    string? PathName,
    string Status,
    int? QuizScore,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? StartedAt);

public record GetEmployeeTrainingQuery(int EmployeeId) : IRequest<List<EmployeeTrainingItem>>;

public class GetEmployeeTrainingHandler(AppDbContext db)
    : IRequestHandler<GetEmployeeTrainingQuery, List<EmployeeTrainingItem>>
{
    public async Task<List<EmployeeTrainingItem>> Handle(GetEmployeeTrainingQuery request, CancellationToken cancellationToken)
    {
        return await db.TrainingProgress
            .Include(p => p.Module)
            .AsNoTracking()
            .Where(p => p.UserId == request.EmployeeId && p.DeletedAt == null)
            .OrderByDescending(p => p.CompletedAt ?? p.StartedAt ?? p.CreatedAt)
            .Select(p => new EmployeeTrainingItem(
                p.Id,
                p.Module.Title,
                p.Module.ContentType.ToString(),
                null,
                p.Status.ToString(),
                p.QuizScore,
                p.CompletedAt,
                p.StartedAt))
            .ToListAsync(cancellationToken);
    }
}
