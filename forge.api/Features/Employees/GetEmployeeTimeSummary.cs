using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

public record EmployeeTimeEntryItem(
    int Id,
    DateOnly Date,
    int DurationMinutes,
    string? Category,
    string? Notes,
    string? JobNumber,
    string? JobTitle,
    bool IsManual,
    DateTimeOffset CreatedAt);

public record GetEmployeeTimeSummaryQuery(int EmployeeId, string? Period) : IRequest<List<EmployeeTimeEntryItem>>;

public class GetEmployeeTimeSummaryHandler(AppDbContext db)
    : IRequestHandler<GetEmployeeTimeSummaryQuery, List<EmployeeTimeEntryItem>>
{
    public async Task<List<EmployeeTimeEntryItem>> Handle(GetEmployeeTimeSummaryQuery request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = request.Period switch
        {
            "week" => DateOnly.FromDateTime(now.AddDays(-7).DateTime),
            "month" => DateOnly.FromDateTime(now.AddMonths(-1).DateTime),
            _ => DateOnly.FromDateTime(now.AddDays(-14).DateTime),
        };

        var entries = await db.TimeEntries
            .Include(t => t.Job)
            .AsNoTracking()
            .Where(t => t.UserId == request.EmployeeId && t.DeletedAt == null && t.Date >= cutoff)
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t => new EmployeeTimeEntryItem(
                t.Id,
                t.Date,
                t.DurationMinutes,
                t.Category,
                t.Notes,
                t.Job != null ? t.Job.JobNumber : null,
                t.Job != null ? t.Job.Title : null,
                t.IsManual,
                t.CreatedAt))
            .ToListAsync(cancellationToken);

        return entries;
    }
}
