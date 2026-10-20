using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

public record GetEmployeeStatsQuery(int EmployeeId) : IRequest<EmployeeStatsResponseModel>;

public class GetEmployeeStatsHandler(AppDbContext db)
    : IRequestHandler<GetEmployeeStatsQuery, EmployeeStatsResponseModel>
{
    public async Task<EmployeeStatsResponseModel> Handle(GetEmployeeStatsQuery request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var periodStart = now.AddDays(-14); // Current 2-week period

        // Hours this period
        var totalMinutes = await db.TimeEntries
            .Where(t => t.UserId == request.EmployeeId && t.DeletedAt == null
                && t.Date >= DateOnly.FromDateTime(periodStart.DateTime))
            .SumAsync(t => t.DurationMinutes, cancellationToken);
        var hoursThisPeriod = Math.Round((decimal)totalMinutes / 60, 1);

        // Compliance
        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == request.EmployeeId, cancellationToken);

        var compliancePercent = EmployeeComplianceRules.PercentComplete(EmployeeComplianceRules.Checklist(profile));

        // Active jobs
        var activeJobCount = await db.Jobs
            .CountAsync(j => j.AssigneeId == request.EmployeeId
                && !j.IsArchived && j.DeletedAt == null
                && j.CompletedDate == null, cancellationToken);

        // Training progress
        var requiredModuleIds = await db.TrainingPathEnrollments
            .Where(e => e.UserId == request.EmployeeId)
            .SelectMany(e => e.Path.PathModules.Where(pm => pm.IsRequired).Select(pm => pm.ModuleId))
            .Distinct()
            .ToListAsync(cancellationToken);
        var trainingRows = await db.TrainingProgress
            .Where(p => p.UserId == request.EmployeeId && p.DeletedAt == null)
            .Select(p => new { p.ModuleId, p.Status })
            .ToListAsync(cancellationToken);
        var completedModuleIds = trainingRows
            .Where(r => r.Status == TrainingProgressStatus.Completed)
            .Select(r => r.ModuleId)
            .ToHashSet();
        var trainingPercent = requiredModuleIds.Count > 0
            ? (int)Math.Round(100.0 * requiredModuleIds.Count(completedModuleIds.Contains) / requiredModuleIds.Count)
            : trainingRows.Count > 0
                ? (int)Math.Round(100.0 * completedModuleIds.Count / trainingRows.Count)
                : 0;

        // Outstanding expenses
        var outstandingExpenses = await db.Expenses
            .Where(e => e.UserId == request.EmployeeId && e.DeletedAt == null
                && e.Status == ExpenseStatus.Pending)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(e => e.Amount) })
            .FirstOrDefaultAsync(cancellationToken);

        return new EmployeeStatsResponseModel(
            hoursThisPeriod,
            compliancePercent,
            activeJobCount,
            trainingPercent,
            outstandingExpenses?.Count ?? 0,
            outstandingExpenses?.Total ?? 0);
    }
}
