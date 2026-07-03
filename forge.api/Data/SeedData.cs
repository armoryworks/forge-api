using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Data;

public static partial class SeedData
{
    public static async Task SeedAsync(IServiceProvider services, bool seedDemoData = true)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<int>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<AppDbContext>();

        // Suppress automatic audit logging during seed to avoid thousands of "Created" entries
        db.SuppressAudit = true;

        // ── 1. Roles (essential — app won't work without these) ──────────
        // Phase 3 / WU-06 / C1 extended catalog from 6 → 11. The 5 new roles
        // (Controller, IT Admin, Procurement, Production Manager, Production
        // Planner) cover small-shop personas the original 6 missed. Existing
        // role names are unchanged for backward compatibility.
        //
        // LeadIntake is a HEADLESS, single-purpose sync role. It is NEVER
        // assigned to a human; it is bound to a seeded service user
        // (lead-intake-system@forge.local) and authenticated via the
        // SystemApiKey scheme (X-Forge-Api-Key header). Grants are scoped to
        // Lead.Create + Lead.Read only — for outbox-style relay of external
        // contact-form submissions and per-row idempotency checks. See
        // docs/api-key-integrations.md.
        string[] roles = [
            "Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager",
            "Controller", "IT Admin", "Procurement", "Production Manager", "Production Planner",
            "LeadIntake",
        ];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<int>(role));
        }

        // ── 1b. Out-of-box role templates (Phase 3 / WU-06 / C1) ─────────
        await SeedRoleTemplatesAsync(db);

        // ── 2. Track Types, Stages & Reference Data (essential) ──────────
        await SeedEssentialDataAsync(db);

        // ── 2b. Calendar taxonomy (compliance-calendar A-1) + legacy backfill ──
        await SeedCalendarTaxonomyAsync(db);

        // ── 2c. Regulatory compliance buckets (compliance-calendar A-5) ──
        await SeedComplianceBucketsAsync(db);

        // ── 2d. Per-industry compliance profiles (regulated-parts-safety C-1) ──
        await SeedComplianceProfilesAsync(db);

        // ── Stop here for clean installs — setup wizard handles user creation ──
        if (!seedDemoData)
        {
            Log.Information("SEED_DEMO_DATA=false — skipping demo data (clean install)");
            return;
        }

        // ══════════════════════════════════════════════════════════════════
        // Everything below is demo/development data only
        // ══════════════════════════════════════════════════════════════════

        // ── 3. Admin user ─────────────────────────────────────────────────
        // Password comes from SEED_USER_PASSWORD env var (set during setup)
        var seedPassword = Environment.GetEnvironmentVariable("SEED_USER_PASSWORD");
        if (string.IsNullOrWhiteSpace(seedPassword))
        {
            Log.Error("SEED_USER_PASSWORD environment variable is required for demo data seeding");
            return;
        }

        var admin = await EnsureUserAsync(userManager, "admin@forge.local",
            "Admin", "User", "AU", "#0d9488", seedPassword, "Admin");

        // ── 4. Team members ───────────────────────────────────────────────
        var akim = await EnsureUserAsync(userManager, "akim@forge.local",
            "A.", "Kim", "AK", "#0d9488", seedPassword, "Engineer");

        var dhart = await EnsureUserAsync(userManager, "dhart@forge.local",
            "D.", "Hart", "DH", "#7c3aed", seedPassword, "Engineer");

        var jsilva = await EnsureUserAsync(userManager, "jsilva@forge.local",
            "J.", "Silva", "JS", "#c2410c", seedPassword, "Engineer");

        var mreyes = await EnsureUserAsync(userManager, "mreyes@forge.local",
            "M.", "Reyes", "MR", "#15803d", seedPassword, "Engineer");

        var pmorris = await EnsureUserAsync(userManager, "pmorris@forge.local",
            "P.", "Morris", "PM", "#0369a1", seedPassword, "PM");

        var lwilson = await EnsureUserAsync(userManager, "lwilson@forge.local",
            "L.", "Wilson", "LW", "#7c3aed", seedPassword, "Manager");

        var cthompson = await EnsureUserAsync(userManager, "cthompson@forge.local",
            "C.", "Thompson", "CT", "#b45309", seedPassword, "OfficeManager");

        var bkelly = await EnsureUserAsync(userManager, "bkelly@forge.local",
            "B.", "Kelly", "BK", "#0f766e", seedPassword, "ProductionWorker");

        // ── 4b. Stress-test users ─────────────────────────────────────────
        // Alpha team — production workers (Production track)
        var alpha1 = await EnsureUserAsync(userManager, "alpha1@forge.local",
            "R.", "Garcia", "RG", "#0284c7", seedPassword, "ProductionWorker");
        var alpha2 = await EnsureUserAsync(userManager, "alpha2@forge.local",
            "T.", "Nguyen", "TN", "#0891b2", seedPassword, "ProductionWorker");
        var alpha3 = await EnsureUserAsync(userManager, "alpha3@forge.local",
            "J.", "Patel", "JP", "#0d9488", seedPassword, "ProductionWorker");
        var alpha4 = await EnsureUserAsync(userManager, "alpha4@forge.local",
            "M.", "Davis", "MD", "#059669", seedPassword, "ProductionWorker");
        var alpha5 = await EnsureUserAsync(userManager, "alpha5@forge.local",
            "K.", "Martinez", "KM", "#16a34a", seedPassword, "ProductionWorker");
        var alpha6 = await EnsureUserAsync(userManager, "alpha6@forge.local",
            "S.", "Lee", "SL", "#65a30d", seedPassword, "ProductionWorker");

        // Bravo team — production workers (Maintenance track)
        var bravo1 = await EnsureUserAsync(userManager, "bravo1@forge.local",
            "D.", "Brown", "DB", "#ca8a04", seedPassword, "ProductionWorker");
        var bravo2 = await EnsureUserAsync(userManager, "bravo2@forge.local",
            "A.", "Taylor", "AT", "#d97706", seedPassword, "ProductionWorker");
        var bravo3 = await EnsureUserAsync(userManager, "bravo3@forge.local",
            "C.", "Anderson", "CA", "#ea580c", seedPassword, "ProductionWorker");
        var bravo4 = await EnsureUserAsync(userManager, "bravo4@forge.local",
            "W.", "Thomas", "WT", "#dc2626", seedPassword, "ProductionWorker");
        var bravo5 = await EnsureUserAsync(userManager, "bravo5@forge.local",
            "E.", "Jackson", "EJ", "#e11d48", seedPassword, "ProductionWorker");
        var bravo6 = await EnsureUserAsync(userManager, "bravo6@forge.local",
            "L.", "White", "LW2", "#c026d3", seedPassword, "ProductionWorker");
        var bravo7 = await EnsureUserAsync(userManager, "bravo7@forge.local",
            "N.", "Harris", "NH", "#9333ea", seedPassword, "ProductionWorker");

        // Second manager for stress test
        var rchavez = await EnsureUserAsync(userManager, "rchavez@forge.local",
            "R.", "Chavez", "RC", "#4f46e5", seedPassword, "Manager");

        await db.SaveChangesAsync();

        // ── 5. Customers ──────────────────────────────────────────────────
        if (!await db.Customers.AnyAsync())
        {
            db.Customers.AddRange(
                new Customer { Name = "Acme Corp" },
                new Customer { Name = "Quantum Dynamics" },
                new Customer { Name = "Apex Manufacturing" },
                new Customer { Name = "Meridian Systems" }
            );
            await db.SaveChangesAsync();
            Log.Information("Seeded customers");
        }

        // ── 6. Jobs ───────────────────────────────────────────────────────
        if (!await db.Jobs.AnyAsync())
        {
            // Look up production track and stages
            var prodTrack = await db.TrackTypes
                .FirstAsync(t => t.Code == "production");

            var stages = await db.JobStages
                .Where(s => s.TrackTypeId == prodTrack.Id)
                .ToDictionaryAsync(s => s.Code);

            var maintTrack = await db.TrackTypes
                .FirstAsync(t => t.Code == "maintenance");

            var maintStages = await db.JobStages
                .Where(s => s.TrackTypeId == maintTrack.Id)
                .ToDictionaryAsync(s => s.Code);

            var customers = await db.Customers.ToListAsync();
            var acme = customers.First(c => c.Name == "Acme Corp");
            var quantum = customers.First(c => c.Name == "Quantum Dynamics");
            var apex = customers.First(c => c.Name == "Apex Manufacturing");
            var meridian = customers.First(c => c.Name == "Meridian Systems");

            var today = DateTimeOffset.UtcNow.Date;
            var yesterday = today.AddDays(-1);

            // Helper
            int pos = 0;
            Job MakeJob(string number, string title, int trackTypeId, int stageId,
                int? assigneeId = null, int? customerId = null, DateTimeOffset? dueDate = null,
                JobPriority priority = JobPriority.Normal)
            {
                return new Job
                {
                    JobNumber = number,
                    Title = title,
                    TrackTypeId = trackTypeId,
                    CurrentStageId = stageId,
                    AssigneeId = assigneeId,
                    CustomerId = customerId,
                    DueDate = dueDate,
                    Priority = priority,
                    BoardPosition = ++pos,
                };
            }

            var jobs = new List<Job>
            {
                // ── Dashboard task list jobs (12 from mock) ───────────────
                // Active tasks
                MakeJob("J-1042", "CNC setup — Bracket Assy Rev C", prodTrack.Id, stages["in_production"].Id,
                    akim.Id, apex.Id, today.AddDays(1)),
                MakeJob("J-1038", "Material prep — Shaft Housing", prodTrack.Id, stages["materials_received"].Id,
                    dhart.Id, quantum.Id, today.AddDays(4)),
                MakeJob("J-1035", "QC inspection — Motor Mount v2", prodTrack.Id, stages["qc_review"].Id,
                    jsilva.Id, acme.Id, today),

                // Next tasks
                MakeJob("J-1041", "Weld fixture alignment check", prodTrack.Id, stages["in_production"].Id,
                    mreyes.Id, meridian.Id),
                MakeJob("J-1039", "Program verify — Plate Adapter", prodTrack.Id, stages["in_production"].Id,
                    akim.Id, apex.Id),
                MakeJob("J-1033", "Deburr & finish — Gear Blank", prodTrack.Id, stages["in_production"].Id,
                    dhart.Id, quantum.Id, yesterday, JobPriority.High),
                MakeJob("J-1040", "First article inspection — Flange", prodTrack.Id, stages["qc_review"].Id,
                    mreyes.Id, acme.Id),
                MakeJob("J-1036", "Assembly — Pneumatic Manifold", prodTrack.Id, stages["in_production"].Id,
                    akim.Id, meridian.Id, today.AddDays(6)),
                MakeJob("J-1037", "Surface grind — Dowel Plate", prodTrack.Id, stages["in_production"].Id,
                    dhart.Id, apex.Id),
                MakeJob("J-1044", "Anodize prep — Heat Sink Array", prodTrack.Id, stages["materials_ordered"].Id,
                    jsilva.Id, quantum.Id),
                MakeJob("J-1045", "Drill & tap — Mounting Block", prodTrack.Id, stages["in_production"].Id,
                    mreyes.Id, meridian.Id),

                // Afternoon tasks from mock
                MakeJob("J-1046", "EDM programming — Die Insert", prodTrack.Id, stages["in_production"].Id,
                    akim.Id, acme.Id),
                MakeJob("J-1034", "Laser mark — Serial plates (50pc)", prodTrack.Id, stages["in_production"].Id,
                    dhart.Id, quantum.Id, today, JobPriority.High),
                MakeJob("J-1047", "Wire EDM — Punch Tool blank", prodTrack.Id, stages["in_production"].Id,
                    akim.Id, apex.Id),
                MakeJob("J-1048", "Tumble finish — Small parts lot", prodTrack.Id, stages["in_production"].Id,
                    dhart.Id, meridian.Id),

                // ── Deadline-specific jobs ────────────────────────────────
                MakeJob("J-1031", "Acme Order — Pack & ship", prodTrack.Id, stages["shipped"].Id,
                    mreyes.Id, acme.Id, today, JobPriority.High),

                // ── Quoting stage jobs (Quoting=3) ────────────────────────
                MakeJob("J-1050", "Custom bracket quote — Meridian", prodTrack.Id, stages["quote_requested"].Id,
                    null, meridian.Id),
                MakeJob("J-1051", "Prototype enclosure estimate", prodTrack.Id, stages["quoted"].Id,
                    akim.Id, quantum.Id),
                MakeJob("J-1052", "Gear assembly RFQ", prodTrack.Id, stages["quoted"].Id,
                    null, apex.Id),

                // ── Planning stage jobs (Planning=5 total: Order Confirmed) ─
                // (J-1038 Materials Received counts separately)
                MakeJob("J-1053", "Motor housing — Order confirmed", prodTrack.Id, stages["order_confirmed"].Id,
                    dhart.Id, acme.Id),
                MakeJob("J-1054", "Connector plate — Planning", prodTrack.Id, stages["order_confirmed"].Id,
                    jsilva.Id, quantum.Id),
                MakeJob("J-1055", "Valve body — Order confirmed", prodTrack.Id, stages["order_confirmed"].Id,
                    akim.Id, meridian.Id),
                MakeJob("J-1056", "Actuator arm — Order confirmed", prodTrack.Id, stages["order_confirmed"].Id,
                    mreyes.Id, apex.Id),
                MakeJob("J-1057", "Bearing cap — Order confirmed", prodTrack.Id, stages["order_confirmed"].Id,
                    dhart.Id, acme.Id),

                // ── Materials stage jobs (Materials=4 total: Ordered + Received) ─
                // J-1044 is already in materials_ordered, J-1038 in materials_received
                MakeJob("J-1058", "Aluminum stock — Heat Sink", prodTrack.Id, stages["materials_ordered"].Id,
                    jsilva.Id, quantum.Id),
                MakeJob("J-1059", "Steel rod — Shaft blanks", prodTrack.Id, stages["materials_received"].Id,
                    dhart.Id, apex.Id),

                // ── QC stage (QC=3 total) ─────────────────────────────────
                // J-1035 and J-1040 already in qc_review
                MakeJob("J-1030", "QC final — Precision bushing", prodTrack.Id, stages["qc_review"].Id,
                    jsilva.Id, acme.Id),

                // ── Shipping stage (Shipping=2) ───────────────────────────
                // J-1031 already in shipped
                MakeJob("J-1060", "Ship — Quantum Dynamics order", prodTrack.Id, stages["shipped"].Id,
                    mreyes.Id, quantum.Id),

                // ── Complete stage (Complete=8: Invoiced/Sent + Payment Received) ─
                MakeJob("J-1020", "Acme bracket order — Complete", prodTrack.Id, stages["invoiced_sent"].Id,
                    akim.Id, acme.Id),
                MakeJob("J-1021", "Shaft assembly — Paid", prodTrack.Id, stages["payment_received"].Id,
                    dhart.Id, quantum.Id),
                MakeJob("J-1022", "Custom flange — Invoiced", prodTrack.Id, stages["invoiced_sent"].Id,
                    jsilva.Id, apex.Id),
                MakeJob("J-1023", "Dowel pin set — Paid", prodTrack.Id, stages["payment_received"].Id,
                    mreyes.Id, meridian.Id),
                MakeJob("J-1024", "Motor mount batch — Invoiced", prodTrack.Id, stages["invoiced_sent"].Id,
                    akim.Id, acme.Id),
                MakeJob("J-1025", "Precision sleeve — Paid", prodTrack.Id, stages["payment_received"].Id,
                    dhart.Id, quantum.Id),
                MakeJob("J-1026", "Manifold adapter — Invoiced", prodTrack.Id, stages["invoiced_sent"].Id,
                    jsilva.Id, apex.Id),
                MakeJob("J-1027", "Coupling insert — Paid", prodTrack.Id, stages["payment_received"].Id,
                    mreyes.Id, meridian.Id),
            };

            // Maintenance job from mock
            jobs.Add(MakeJob("M-0012", "Machine clean & PM — Haas VF-2", maintTrack.Id, maintStages["scheduled"].Id,
                mreyes.Id));

            db.Jobs.AddRange(jobs);
            await db.SaveChangesAsync();

            // ── Subtask: Tool change on J-1042 ───────────────────────────
            var j1042 = jobs.First(j => j.JobNumber == "J-1042");
            db.JobSubtasks.Add(new JobSubtask
            {
                JobId = j1042.Id,
                Text = "Tool change — End Mill #4 replace",
                AssigneeId = jsilva.Id,
                SortOrder = 1,
            });
            await db.SaveChangesAsync();

            Log.Information("Seeded {Count} jobs", jobs.Count);
        }

        // Reference data is now seeded by SeedEssentialDataAsync above

        // ── 8. Activity Log ──────────────────────────────────────────────
        if (!await db.JobActivityLogs.AnyAsync())
        {
            var now = DateTimeOffset.UtcNow;

            // Look up job IDs for activity entries
            var j1042 = await db.Jobs.FirstAsync(j => j.JobNumber == "J-1042");
            var j1044 = await db.Jobs.FirstAsync(j => j.JobNumber == "J-1044");
            var j1038 = await db.Jobs.FirstAsync(j => j.JobNumber == "J-1038");
            var j1030 = await db.Jobs.FirstAsync(j => j.JobNumber == "J-1030");
            var j1034 = await db.Jobs.FirstAsync(j => j.JobNumber == "J-1034");

            db.JobActivityLogs.AddRange(
                new JobActivityLog
                {
                    JobId = j1042.Id,
                    UserId = admin.Id,
                    Action = ActivityAction.StageMoved,
                    FieldName = "CurrentStageId",
                    OldValue = "Materials Received",
                    NewValue = "In Production",
                    Description = "J-1042 moved to Production",
                    CreatedAt = now.AddMinutes(-10),
                },
                new JobActivityLog
                {
                    JobId = j1044.Id,
                    UserId = admin.Id,
                    Action = ActivityAction.Assigned,
                    FieldName = "AssigneeId",
                    NewValue = akim.Id.ToString(),
                    Description = "A. Kim assigned to J-1044",
                    CreatedAt = now.AddMinutes(-25),
                },
                new JobActivityLog
                {
                    JobId = j1038.Id,
                    UserId = dhart.Id,
                    Action = ActivityAction.FieldChanged,
                    FieldName = "Attachment",
                    NewValue = "drawing_v2.pdf",
                    Description = "Drawing uploaded to J-1038",
                    CreatedAt = now.AddHours(-1),
                },
                new JobActivityLog
                {
                    JobId = j1030.Id,
                    UserId = jsilva.Id,
                    Action = ActivityAction.StageMoved,
                    FieldName = "CurrentStageId",
                    OldValue = "In Production",
                    NewValue = "QC/Review",
                    Description = "J-1030 passed QC inspection",
                    CreatedAt = now.AddHours(-2),
                },
                new JobActivityLog
                {
                    JobId = j1034.Id,
                    UserId = null,
                    Action = ActivityAction.FieldChanged,
                    FieldName = "DueDate",
                    Description = "J-1034 overdue — was due yesterday",
                    CreatedAt = now.AddHours(-3),
                }
            );
            await db.SaveChangesAsync();
            Log.Information("Seeded activity log entries");
        }

        // ── 9. Pre-packaged Reports ─────────────────────────────────────
        if (!await db.SavedReports.AnyAsync())
        {
            var adminUser = await userManager.FindByEmailAsync("admin@forge.local");
            var adminId = adminUser!.Id;

            db.SavedReports.AddRange(
                // ── My Reports (user-scoped at runtime, seeded as shared templates) ──
                new SavedReport
                {
                    Name = "My Work History",
                    Description = "Your assigned jobs with stage, customer, and dates",
                    EntitySource = "Jobs",
                    ColumnsJson = """["JobNumber","Title","Customer.Name","CurrentStage.Name","Priority","DueDate","CompletedDate","CreatedAt"]""",
                    SortField = "CreatedAt", SortDirection = "desc",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "My Time Log",
                    Description = "Your time entries with job and duration details",
                    EntitySource = "TimeEntries",
                    ColumnsJson = """["Date","Job.JobNumber","Job.Title","Category","DurationMinutes","Notes","IsManual"]""",
                    SortField = "Date", SortDirection = "desc",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "My Expense History",
                    Description = "Your expense submissions with status and amounts",
                    EntitySource = "Expenses",
                    ColumnsJson = """["ExpenseDate","Category","Description","Amount","Status","Job.JobNumber"]""",
                    SortField = "ExpenseDate", SortDirection = "desc",
                    IsShared = true, UserId = adminId,
                },
                // ── Job Reports ──
                new SavedReport
                {
                    Name = "Jobs by Stage",
                    Description = "All jobs grouped by their current stage",
                    EntitySource = "Jobs",
                    ColumnsJson = """["JobNumber","Title","Customer.Name","CurrentStage.Name","Priority","DueDate","TrackType.Name"]""",
                    GroupByField = "CurrentStage.Name",
                    SortField = "CurrentStage.Name", SortDirection = "asc",
                    ChartType = "bar", ChartLabelField = "CurrentStage.Name", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Overdue Jobs",
                    Description = "Jobs past their due date",
                    EntitySource = "Jobs",
                    ColumnsJson = """["JobNumber","Title","Customer.Name","CurrentStage.Name","Priority","DueDate","TrackType.Name"]""",
                    FiltersJson = """[{"Field":"DueDate","Operator":"LessThan","Value":"now"},{"Field":"CompletedDate","Operator":"IsNull"}]""",
                    SortField = "DueDate", SortDirection = "asc",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Job Completion Trend",
                    Description = "Jobs created vs completed over time",
                    EntitySource = "Jobs",
                    ColumnsJson = """["JobNumber","Title","CreatedAt","CompletedDate","Customer.Name","TrackType.Name"]""",
                    SortField = "CreatedAt", SortDirection = "desc",
                    ChartType = "line", ChartLabelField = "CreatedAt", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "On-Time Delivery Rate",
                    Description = "Completed jobs analyzed by on-time vs late delivery",
                    EntitySource = "Jobs",
                    ColumnsJson = """["JobNumber","Title","DueDate","CompletedDate","Customer.Name","CurrentStage.Name"]""",
                    FiltersJson = """[{"Field":"CompletedDate","Operator":"IsNotNull"}]""",
                    SortField = "CompletedDate", SortDirection = "desc",
                    ChartType = "pie", ChartLabelField = "TrackType.Name", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Average Lead Time",
                    Description = "Average time jobs spend in each stage",
                    EntitySource = "Jobs",
                    ColumnsJson = """["JobNumber","Title","CurrentStage.Name","CreatedAt","CompletedDate","TrackType.Name"]""",
                    GroupByField = "CurrentStage.Name",
                    ChartType = "bar", ChartLabelField = "CurrentStage.Name", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Time in Stage (Bottleneck)",
                    Description = "Identify bottleneck stages where jobs spend the most time",
                    EntitySource = "Jobs",
                    ColumnsJson = """["JobNumber","Title","CurrentStage.Name","DueDate","CreatedAt","Priority"]""",
                    GroupByField = "CurrentStage.Name",
                    SortField = "CurrentStage.Name", SortDirection = "asc",
                    ChartType = "bar", ChartLabelField = "CurrentStage.Name", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "R&D Reports",
                    Description = "R&D jobs with iterations, hours, and current stage",
                    EntitySource = "Jobs",
                    ColumnsJson = """["JobNumber","Title","IterationCount","CurrentStage.Name","IsInternal","CreatedAt","CompletedDate"]""",
                    FiltersJson = """[{"Field":"IsInternal","Operator":"Equals","Value":"true"}]""",
                    SortField = "CreatedAt", SortDirection = "desc",
                    IsShared = true, UserId = adminId,
                },
                // ── Team & Labor Reports ──
                new SavedReport
                {
                    Name = "Team Workload",
                    Description = "Active and overdue jobs per team member",
                    EntitySource = "Jobs",
                    ColumnsJson = """["JobNumber","Title","Priority","DueDate","CurrentStage.Name","IsArchived","Customer.Name"]""",
                    FiltersJson = """[{"Field":"IsArchived","Operator":"Equals","Value":"false"}]""",
                    SortField = "Priority", SortDirection = "asc",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Employee Productivity",
                    Description = "Time entries and job completions per employee",
                    EntitySource = "TimeEntries",
                    ColumnsJson = """["Date","Job.JobNumber","Job.Title","Category","DurationMinutes","IsManual"]""",
                    GroupByField = "Category",
                    SortField = "Date", SortDirection = "desc",
                    ChartType = "bar", ChartLabelField = "Category", ChartValueField = "DurationMinutes",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Labor Hours by Job",
                    Description = "Total time entries grouped by job",
                    EntitySource = "TimeEntries",
                    ColumnsJson = """["Job.JobNumber","Job.Title","Date","DurationMinutes","Category","Notes"]""",
                    GroupByField = "Job.JobNumber",
                    SortField = "DurationMinutes", SortDirection = "desc",
                    ChartType = "bar", ChartLabelField = "Job.JobNumber", ChartValueField = "DurationMinutes",
                    IsShared = true, UserId = adminId,
                },
                // ── Financial Reports ──
                new SavedReport
                {
                    Name = "Expense Summary",
                    Description = "Expenses grouped by category with totals",
                    EntitySource = "Expenses",
                    ColumnsJson = """["ExpenseDate","Category","Description","Amount","Status","Job.JobNumber"]""",
                    GroupByField = "Category",
                    SortField = "ExpenseDate", SortDirection = "desc",
                    ChartType = "pie", ChartLabelField = "Category", ChartValueField = "Amount",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Invoice Summary",
                    Description = "All invoices with status, dates, and customer",
                    EntitySource = "Invoices",
                    ColumnsJson = """["InvoiceNumber","Customer.Name","Status","InvoiceDate","DueDate","TaxRate","CreatedAt"]""",
                    GroupByField = "Status",
                    SortField = "InvoiceDate", SortDirection = "desc",
                    ChartType = "bar", ChartLabelField = "Status", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                // ── Customer & Sales Reports ──
                new SavedReport
                {
                    Name = "Customer Activity",
                    Description = "Job activity per customer (active, completed, total)",
                    EntitySource = "Jobs",
                    ColumnsJson = """["Customer.Name","JobNumber","Title","CurrentStage.Name","Priority","DueDate","CompletedDate"]""",
                    GroupByField = "Customer.Name",
                    SortField = "Customer.Name", SortDirection = "asc",
                    ChartType = "bar", ChartLabelField = "Customer.Name", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Quote-to-Close Rate",
                    Description = "Quote conversion analysis — sent vs accepted vs expired",
                    EntitySource = "Quotes",
                    ColumnsJson = """["QuoteNumber","Customer.Name","Status","SentDate","AcceptedDate","ExpirationDate","CreatedAt"]""",
                    GroupByField = "Status",
                    SortField = "CreatedAt", SortDirection = "desc",
                    ChartType = "pie", ChartLabelField = "Status", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Lead Pipeline",
                    Description = "Leads grouped by status through the pipeline",
                    EntitySource = "Leads",
                    ColumnsJson = """["CompanyName","ContactName","Source","Status","FollowUpDate","CreatedAt"]""",
                    GroupByField = "Status",
                    SortField = "CreatedAt", SortDirection = "desc",
                    ChartType = "bar", ChartLabelField = "Status", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Lead & Sales Summary",
                    Description = "Lead sources and conversion metrics",
                    EntitySource = "Leads",
                    ColumnsJson = """["CompanyName","ContactName","Source","Status","Email","Phone","CreatedAt"]""",
                    GroupByField = "Source",
                    SortField = "Source", SortDirection = "asc",
                    ChartType = "bar", ChartLabelField = "Source", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                // ── Inventory & Supply Chain ──
                new SavedReport
                {
                    Name = "Inventory Levels",
                    Description = "Bin content levels by location",
                    EntitySource = "Inventory",
                    ColumnsJson = """["Location.Name","EntityType","Quantity","ReservedQuantity","LotNumber","Status","PlacedAt"]""",
                    GroupByField = "Location.Name",
                    SortField = "Location.Name", SortDirection = "asc",
                    ChartType = "bar", ChartLabelField = "Location.Name", ChartValueField = "Quantity",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Shipping Summary",
                    Description = "Shipments by carrier and status",
                    EntitySource = "Shipments",
                    ColumnsJson = """["ShipmentNumber","SalesOrder.OrderNumber","Carrier","Status","ShippedDate","DeliveredDate","ShippingCost","TrackingNumber"]""",
                    GroupByField = "Carrier",
                    SortField = "ShippedDate", SortDirection = "desc",
                    ChartType = "bar", ChartLabelField = "Carrier", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Purchase Order Summary",
                    Description = "Purchase orders by status and vendor",
                    EntitySource = "PurchaseOrders",
                    ColumnsJson = """["PONumber","Vendor.Name","Status","SubmittedDate","ExpectedDeliveryDate","ReceivedDate"]""",
                    GroupByField = "Status",
                    SortField = "CreatedAt", SortDirection = "desc",
                    ChartType = "bar", ChartLabelField = "Status", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Sales Order Summary",
                    Description = "Sales orders by status and customer",
                    EntitySource = "SalesOrders",
                    ColumnsJson = """["OrderNumber","Customer.Name","Status","ConfirmedDate","RequestedDeliveryDate","CustomerPO"]""",
                    GroupByField = "Status",
                    SortField = "CreatedAt", SortDirection = "desc",
                    ChartType = "bar", ChartLabelField = "Status", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                // ── Operations ──
                new SavedReport
                {
                    Name = "Maintenance Reports",
                    Description = "Asset maintenance, downtime hours, and reliability",
                    EntitySource = "Assets",
                    ColumnsJson = """["Name","AssetType","Status","Location","Manufacturer","CurrentHours","CavityCount","CurrentShotCount","CreatedAt"]""",
                    GroupByField = "AssetType",
                    SortField = "CurrentHours", SortDirection = "desc",
                    ChartType = "bar", ChartLabelField = "Name", ChartValueField = "CurrentHours",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Quality / Scrap Rate",
                    Description = "Parts and jobs with quality inspection outcomes",
                    EntitySource = "Parts",
                    ColumnsJson = """["PartNumber","Description","Status","ProcurementSource","InventoryClass","Revision","CreatedAt"]""",
                    GroupByField = "Status",
                    SortField = "PartNumber", SortDirection = "asc",
                    ChartType = "pie", ChartLabelField = "Status", ChartValueField = "Id",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Customer List",
                    Description = "All customers with contact information and activity status",
                    EntitySource = "Customers",
                    ColumnsJson = """["Name","CompanyName","Email","Phone","IsActive","CreatedAt"]""",
                    SortField = "Name", SortDirection = "asc",
                    IsShared = true, UserId = adminId,
                },
                new SavedReport
                {
                    Name = "Parts Catalog",
                    Description = "Complete parts listing with revision and stock info",
                    EntitySource = "Parts",
                    ColumnsJson = """["PartNumber","Description","Revision","Status","ProcurementSource","InventoryClass","MinStockThreshold","ReorderPoint"]""",
                    SortField = "PartNumber", SortDirection = "asc",
                    IsShared = true, UserId = adminId,
                }
            );

            await db.SaveChangesAsync();
            Log.Information("Seeded {Count} pre-packaged reports", 27);
        }

        // ── Sales Tax Rates ────────────────────────────────────────────────
        await SeedSalesTaxRatesAsync(db);

        // ── Default Chat Channels ────────────────────────────────────────────
        await SeedDefaultChannelsAsync(db, admin.Id);

        // ── Accounting GL Phase-0 foundation (dark; CAP-ACCT-FULLGL off) ─────
        // Book + chart of accounts + determination rules + current fiscal
        // year/periods. Idempotent (run-once on empty acct_books). See §5.4.
        await SeedAccountingAsync(db);

        // ── Historical Data ────────────────────────────────────────────────
        await SeedHistoricalDataAsync(db, admin.Id, akim.Id, dhart.Id, jsilva.Id, mreyes.Id,
            pmorris.Id, lwilson.Id, cthompson.Id, bkelly.Id);

        // ── Job Number Sequence ───────────────────────────────────────────
        // The job_number_seq sequence is created by SeedEssentialDataAsync
        // (always-run, so clean installs work too). Here we just advance it
        // past the demo jobs we seeded so the next created job doesn't reuse a
        // J-XXXX number. Derived from the highest existing J-XXXX job number.
        var maxJobNum = await db.Jobs
            .Where(j => j.JobNumber != null && j.JobNumber.StartsWith("J-"))
            .Select(j => j.JobNumber)
            .ToListAsync();
        var maxNum = maxJobNum
            .Select(jn => int.TryParse(jn.Replace("J-", ""), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        if (maxNum > 0)
        {
            // setval doesn't support parameterized queries — value is a safe integer from our own MAX()
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"SELECT setval('job_number_seq', {maxNum})");
#pragma warning restore EF1002
            Log.Information("Advanced job_number_seq (next value: {NextVal})", maxNum + 1);
        }

        Log.Information("Database seeding complete");
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email, string firstName, string lastName,
        string initials, string avatarColor,
        string password, string role)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            // Always re-apply the seed password so reseeds are idempotent regardless
            // of how the account was originally created (setup wizard vs. seed).
            await userManager.RemovePasswordAsync(existing);
            await userManager.AddPasswordAsync(existing, password);
            return existing;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            Initials = initials,
            AvatarColor = avatarColor,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, role);
            Log.Information("Created user: {Email} ({Role})", email, role);
        }
        else
        {
            Log.Error("Failed to create user {Email}: {Errors}", email,
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        return user;
    }

    private static async Task SeedRoleTemplatesAsync(AppDbContext db)
    {
        // Idempotent — only inserts a system-default template when no row
        // with that Name exists. Tenant edits to a system default are NOT
        // overwritten by re-running the seed.
        var defaults = new[]
        {
            new RoleTemplate
            {
                Name = "FrontOffice",
                Description = "Back-office rollup: Office Manager + Controller + IT Admin. " +
                              "For small shops where one person handles all back-office work.",
                IsSystemDefault = true,
                IncludedRoleNamesJson = """["OfficeManager","Controller","IT Admin"]""",
            },
            new RoleTemplate
            {
                Name = "FloorLead",
                Description = "Production rollup: PM + Production Manager + Production Planner. " +
                              "For shops where one person coordinates all floor-side work.",
                IsSystemDefault = true,
                IncludedRoleNamesJson = """["PM","Production Manager","Production Planner"]""",
            },
            new RoleTemplate
            {
                Name = "OwnerOperator",
                Description = "Owner rollup: Admin + Manager + Controller. " +
                              "For solo / micro shops where the owner operates everywhere.",
                IsSystemDefault = true,
                IncludedRoleNamesJson = """["Admin","Manager","Controller"]""",
            },
        };

        foreach (var tmpl in defaults)
        {
            if (!await db.RoleTemplates.AnyAsync(t => t.Name == tmpl.Name))
                db.RoleTemplates.Add(tmpl);
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync();
            Log.Information("Seeded {Count} default role templates", defaults.Length);
        }
    }

    private static async Task SeedDefaultChannelsAsync(AppDbContext db, int adminId)
    {
        if (await db.Set<ChatRoom>().AnyAsync(r => r.ChannelType == ChannelType.System))
            return;

        var allUserIds = await db.Users
            .Where(u => u.IsActive)
            .Select(u => u.Id)
            .ToListAsync();

        // General channel — company-wide, everyone auto-joined
        var general = new ChatRoom
        {
            Name = "General",
            IsGroup = true,
            CreatedById = adminId,
            ChannelType = ChannelType.System,
            Description = "Company-wide general discussion",
            IconName = "forum",
            CreatedBySystem = true,
        };
        foreach (var userId in allUserIds)
        {
            general.Members.Add(new ChatRoomMember
            {
                UserId = userId,
                JoinedAt = DateTimeOffset.UtcNow,
                Role = userId == adminId ? ChannelMemberRole.Owner : ChannelMemberRole.Member,
            });
        }

        // Announcements channel — read-only broadcast
        var announcements = new ChatRoom
        {
            Name = "Announcements",
            IsGroup = true,
            CreatedById = adminId,
            ChannelType = ChannelType.Broadcast,
            Description = "Official company announcements (read-only)",
            IconName = "campaign",
            IsReadOnly = true,
            CreatedBySystem = true,
        };
        foreach (var userId in allUserIds)
        {
            announcements.Members.Add(new ChatRoomMember
            {
                UserId = userId,
                JoinedAt = DateTimeOffset.UtcNow,
                Role = userId == adminId ? ChannelMemberRole.Owner : ChannelMemberRole.Member,
            });
        }

        db.Set<ChatRoom>().AddRange(general, announcements);

        // Auto-create team channels
        var teams = await db.Set<Team>()
            .Where(t => t.IsActive)
            .ToListAsync();

        foreach (var team in teams)
        {
            var existingTeamChannel = await db.Set<ChatRoom>()
                .AnyAsync(r => r.TeamId == team.Id && r.ChannelType == ChannelType.TeamAuto);

            if (existingTeamChannel) continue;

            var teamChannel = new ChatRoom
            {
                Name = team.Name,
                IsGroup = true,
                CreatedById = adminId,
                ChannelType = ChannelType.TeamAuto,
                Description = $"Auto-created channel for {team.Name} team",
                IconName = "group",
                TeamId = team.Id,
                CreatedBySystem = true,
            };

            // Add admin as owner — team members will be synced separately
            teamChannel.Members.Add(new ChatRoomMember
            {
                UserId = adminId,
                JoinedAt = DateTimeOffset.UtcNow,
                Role = ChannelMemberRole.Owner,
            });

            db.Set<ChatRoom>().Add(teamChannel);
        }

        await db.SaveChangesAsync();
        Log.Information("Seeded default chat channels (General, Announcements, {TeamCount} team channels)", teams.Count);
    }
}

