using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Tests.Reports;

/// <summary>
/// WU-15 / RPT-EMPLABOR-001 — contract test for /reports/employee-productivity.
///
/// Phase 1 found this endpoint returned [] despite 720+ time entries because
/// the controller bound missing date params to DateTimeOffset.MinValue. Fix:
/// controller now defaults to a trailing 12-month window. This test guards
/// the contract: seeded time entries in-window must surface in aggregation.
/// </summary>
public class EmployeeProductivityContractTests : ReportContractTestBase
{
    protected override string EndpointName => "/reports/employee-productivity";

    protected override async Task SeedAsync(AppDbContext db)
    {
        var user = new ApplicationUser
        {
            UserName = "rpt.user@example.com",
            Email = "rpt.user@example.com",
            FirstName = "Report",
            LastName = "User",
            IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Two entries clearly inside the trailing-12-month window.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.TimeEntries.AddRange(
            new TimeEntry
            {
                UserId = user.Id,
                Date = today.AddDays(-7),
                DurationMinutes = 240,
                Category = "Production",
            },
            new TimeEntry
            {
                UserId = user.Id,
                Date = today.AddDays(-1),
                DurationMinutes = 360,
                Category = "Production",
            });
        await db.SaveChangesAsync();
    }

    protected override async Task<object?> InvokeAsync(AppDbContext db)
    {
        var repo = RepoFor(db);
        var end = DateTimeOffset.UtcNow;
        var start = end.AddMonths(-12);
        return await repo.GetEmployeeProductivityAsync(start, end, CancellationToken.None);
    }
}
