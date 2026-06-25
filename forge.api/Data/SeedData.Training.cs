using Microsoft.EntityFrameworkCore;

using Forge.Api.Data.TrainingContent;
using Forge.Core.Entities;
using Forge.Data.Context;

using Serilog;

namespace Forge.Api.Data;

public static partial class SeedData
{
    private static async Task SeedTrainingAsync(AppDbContext db)
    {
        var slugMap = await TrainingContentBase.LoadSlugMapAsync(db);

        // ── Seed all feature modules (alphabetical) ─────────────────────
        var seeders = new TrainingContentBase[]
        {
            new AccountingTraining(db, slugMap),
            new AdminTraining(db, slugMap),
            new AiTraining(db, slugMap),
            new ApprovalsTraining(db, slugMap),
            new AssetsTraining(db, slugMap),
            new BacklogTraining(db, slugMap),
            new CalendarTraining(db, slugMap),
            new ChatTraining(db, slugMap),
            new ComplianceTraining(db, slugMap),
            new CustomerReturnsTraining(db, slugMap),
            new CustomersTraining(db, slugMap),
            new DashboardTraining(db, slugMap),
            new EdiTraining(db, slugMap),
            new EmployeesTraining(db, slugMap),
            new EstimatesTraining(db, slugMap),
            new EventsTraining(db, slugMap),
            new ExpensesTraining(db, slugMap),
            new InventoryTraining(db, slugMap),
            new InvoicesTraining(db, slugMap),
            new KanbanTraining(db, slugMap),
            new LeadsTraining(db, slugMap),
            new MaintenanceTraining(db, slugMap),
            new MfaTraining(db, slugMap),
            new MrpTraining(db, slugMap),
            new NavigationTraining(db, slugMap),
            new NotificationsTraining(db, slugMap),
            new OeeTraining(db, slugMap),
            new OnboardingTraining(db, slugMap),
            new PageSpecificTraining(db, slugMap),
            new PartsTraining(db, slugMap),
            new PayablesTraining(db, slugMap),
            new PaymentsTraining(db, slugMap),
            new PayrollTraining(db, slugMap),
            new PlanningTraining(db, slugMap),
            new ProductionLotsTraining(db, slugMap),
            new PurchaseOrdersTraining(db, slugMap),
            new PurchasingTraining(db, slugMap),
            new QualityTraining(db, slugMap),
            new QuotesTraining(db, slugMap),
            new ReportsTraining(db, slugMap),
            new SalesOrdersTraining(db, slugMap),
            new SchedulingTraining(db, slugMap),
            new SearchTraining(db, slugMap),
            new ShipmentsTraining(db, slugMap),
            new ShopFloorTraining(db, slugMap),
            new TimeTrackingTraining(db, slugMap),
            new VendorsTraining(db, slugMap),
        };

        foreach (var seeder in seeders)
        {
            await seeder.SeedAsync();
        }

        Log.Information("Seeded {Count} training modules across {Seeders} features",
            slugMap.Count, seeders.Length);

        // ── Seed training paths ─────────────────────────────────────────
        var pathDefs = new PathDefinitions(db, slugMap);
        await pathDefs.SeedPathsAsync();

        // ── Back-fill enrollments for existing users ─────────────────────
        await BackfillEnrollmentsAsync(db);
    }

    /// <summary>
    /// No longer needed — paths are now defined in PathDefinitions.cs.
    /// Kept as a no-op for backward compatibility with SeedData.Essential.cs call site.
    /// </summary>
    private static Task SeedAdditionalTrainingPathsAsync(AppDbContext db) => Task.CompletedTask;

    private static async Task BackfillEnrollmentsAsync(AppDbContext db)
    {
        var autoPaths = await db.TrainingPaths
            .Where(p => p.IsAutoAssigned && p.IsActive && p.DeletedAt == null)
            .ToListAsync();

        if (autoPaths.Count == 0) return;

        var existingEnrollments = await db.TrainingPathEnrollments
            .Select(e => new { e.UserId, e.PathId })
            .ToListAsync();

        var users = await db.Users
            .Select(u => new { u.Id })
            .ToListAsync();

        var userRoles = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, RoleName = r.Name ?? "" })
            .ToListAsync();

        var rolesByUser = userRoles
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.RoleName).ToArray());

        var newEnrollments = 0;
        foreach (var path in autoPaths)
        {
            var allowedRoles = string.IsNullOrEmpty(path.AllowedRoles)
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<string[]>(path.AllowedRoles);

            foreach (var user in users)
            {
                if (existingEnrollments.Any(e => e.UserId == user.Id && e.PathId == path.Id))
                    continue;

                var userRoleNames = rolesByUser.TryGetValue(user.Id, out var roles) ? roles : [];
                if (allowedRoles != null && !userRoleNames.Any(r => allowedRoles.Contains(r)))
                    continue;

                db.TrainingPathEnrollments.Add(new TrainingPathEnrollment
                {
                    UserId = user.Id,
                    PathId = path.Id,
                    IsAutoAssigned = true,
                });
                newEnrollments++;
            }
        }

        if (newEnrollments > 0)
        {
            await db.SaveChangesAsync();
            Log.Information("Back-filled {Count} training enrollments for existing users", newEnrollments);
        }
    }
}
