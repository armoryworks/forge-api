using Forge.Core.Entities;
using Forge.Data.Context;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace Forge.Api.Data.TrainingContent;

public class PathDefinitions
{
    private readonly AppDbContext _db;
    private readonly Dictionary<string, int> _slugMap;

    public PathDefinitions(AppDbContext db, Dictionary<string, int> slugMap)
    {
        _db = db;
        _slugMap = slugMap;
    }

    private int Lookup(string slug) => _slugMap.TryGetValue(slug, out var id) ? id : 0;

    public async Task SeedPathsAsync()
    {
        // ── Path 1: New Employee Onboarding ─────────────────────────────
        await SeedPath(new TrainingPath
        {
            Title = "New Employee Onboarding",
            Slug = "new-employee-onboarding",
            Description = "Essential orientation for every new employee: navigation, profile setup, compliance, time tracking, and expenses.",
            Icon = "waving_hand",
            IsAutoAssigned = true,
            IsActive = true,
            SortOrder = 1,
            AllowedRoles = null,
        }, [
            ("welcome-to-forge", true),
            ("navigating-the-app-overview", true),
            ("navigating-the-app", true),
            ("setting-up-your-profile", true),
            ("compliance-forms-overview", true),
            ("compliance-forms-walkthrough", true),
            ("time-tracking-overview", true),
            ("time-tracking-walkthrough", true),
            ("expenses-overview", true),
            ("expenses-walkthrough", true),
            ("notifications-overview", false),
            ("search-overview", false),
            ("onboarding-quiz", true),
        ]);

        // ── Path 2: Production Engineer ─────────────────────────────────
        await SeedPath(new TrainingPath
        {
            Title = "Production Engineer",
            Slug = "production-engineer",
            Description = "Deep training for engineers: kanban board, parts catalog with BOMs, quality inspections, and inventory management.",
            Icon = "precision_manufacturing",
            IsAutoAssigned = false,
            IsActive = true,
            SortOrder = 2,
            AllowedRoles = """["Admin","Manager","Engineer"]""",
        }, [
            ("kanban-overview", true),
            ("kanban-walkthrough", true),
            ("kanban-field-reference", false),
            ("kanban-quiz", true),
            ("parts-overview", true),
            ("parts-walkthrough", true),
            ("parts-field-reference", false),
            ("parts-quiz", true),
            ("quality-overview", true),
            ("quality-walkthrough", true),
            ("quality-field-reference", false),
            ("quality-quiz", true),
            ("inventory-overview", true),
            ("inventory-walkthrough", false),
        ]);

        // ── Path 3: Shop Floor Worker ───────────────────────────────────
        await SeedPath(new TrainingPath
        {
            Title = "Shop Floor Worker",
            Slug = "shop-floor-worker",
            Description = "Training for production workers: shop floor kiosk, timers, barcode scanning, and basic job actions.",
            Icon = "engineering",
            IsAutoAssigned = false,
            IsActive = true,
            SortOrder = 3,
            AllowedRoles = """["Admin","Manager","Engineer","ProductionWorker"]""",
        }, [
            ("shop-floor-overview", true),
            ("shop-floor-walkthrough", true),
            ("shop-floor-field-reference", false),
            ("shop-floor-quiz", true),
            ("time-tracking-overview", true),
            ("time-tracking-walkthrough", true),
            ("time-tracking-quiz", true),
        ]);

        // ── Path 4: Project Manager ─────────────────────────────────────
        await SeedPath(new TrainingPath
        {
            Title = "Project Manager",
            Slug = "project-manager",
            Description = "Training for PMs: backlog management, planning cycles, reporting, calendar, and dashboard widgets.",
            Icon = "assignment",
            IsAutoAssigned = false,
            IsActive = true,
            SortOrder = 4,
            AllowedRoles = """["Admin","Manager","PM"]""",
        }, [
            ("backlog-overview", true),
            ("backlog-walkthrough", true),
            ("backlog-field-reference", false),
            ("backlog-quiz", true),
            ("planning-overview", true),
            ("planning-walkthrough", true),
            ("planning-field-reference", false),
            ("planning-quiz", true),
            ("reports-overview", true),
            ("reports-walkthrough", true),
            ("reports-field-reference", false),
            ("reports-quiz", true),
            ("calendar-overview", true),
            ("calendar-walkthrough", false),
            ("dashboard-overview", true),
            ("dashboard-walkthrough", false),
        ]);

        // ── Path 5: Office Manager ──────────────────────────────────────
        await SeedPath(new TrainingPath
        {
            Title = "Office Manager",
            Slug = "office-manager",
            Description = "Training for office managers: customers, estimates, quotes, sales orders, invoices, payments, and shipments.",
            Icon = "storefront",
            IsAutoAssigned = false,
            IsActive = true,
            SortOrder = 5,
            AllowedRoles = """["Admin","Manager","OfficeManager"]""",
        }, [
            ("customers-overview", true),
            ("customers-walkthrough", true),
            ("customers-field-reference", false),
            ("customers-quiz", true),
            ("estimates-overview", true),
            ("estimates-walkthrough", true),
            ("estimates-quiz", true),
            ("quotes-overview", true),
            ("quotes-walkthrough", true),
            ("quotes-field-reference", false),
            ("quotes-quiz", true),
            ("sales-orders-overview", true),
            ("sales-orders-walkthrough", true),
            ("sales-orders-field-reference", false),
            ("sales-orders-quiz", true),
            ("invoices-overview", true),
            ("invoices-walkthrough", true),
            ("invoices-quiz", true),
            ("payments-overview", true),
            ("payments-walkthrough", true),
            ("payments-quiz", true),
            ("shipments-overview", true),
            ("shipments-walkthrough", true),
            ("shipments-quiz", true),
        ]);

        // ── Path 6: Purchasing ──────────────────────────────────────────
        await SeedPath(new TrainingPath
        {
            Title = "Purchasing",
            Slug = "purchasing",
            Description = "Training for purchasing staff: vendors, purchase orders, receiving, and inventory management.",
            Icon = "shopping_cart",
            IsAutoAssigned = false,
            IsActive = true,
            SortOrder = 6,
            AllowedRoles = """["Admin","Manager","OfficeManager"]""",
        }, [
            ("vendors-overview", true),
            ("vendors-walkthrough", true),
            ("vendors-field-reference", false),
            ("vendors-quiz", true),
            ("purchase-orders-overview", true),
            ("purchase-orders-walkthrough", true),
            ("purchase-orders-field-reference", false),
            ("purchase-orders-quiz", true),
            ("inventory-overview", true),
            ("inventory-walkthrough", true),
            ("inventory-field-reference", false),
            ("inventory-quiz", true),
        ]);

        // ── Path 7: System Admin ────────────────────────────────────────
        await SeedPath(new TrainingPath
        {
            Title = "System Administrator",
            Slug = "system-admin",
            Description = "Training for admins: user management, system settings, EDI, events, MFA policies, AI assistants, and audit logs.",
            Icon = "admin_panel_settings",
            IsAutoAssigned = false,
            IsActive = true,
            SortOrder = 7,
            AllowedRoles = """["Admin"]""",
        }, [
            ("admin-overview", true),
            ("admin-walkthrough", true),
            ("admin-field-reference", false),
            ("admin-quiz", true),
            ("edi-overview", true),
            ("edi-walkthrough", true),
            ("edi-quiz", true),
            ("events-overview", true),
            ("events-walkthrough", true),
            ("events-quiz", true),
            ("mfa-overview", true),
            ("mfa-walkthrough", true),
            ("mfa-quiz", true),
            ("ai-overview", false),
            ("ai-walkthrough", false),
        ]);

        // ── Path 8: Sales ───────────────────────────────────────────────
        await SeedPath(new TrainingPath
        {
            Title = "Sales",
            Slug = "sales",
            Description = "Training for sales staff: leads pipeline, customer management, quoting, sales orders, and shipments.",
            Icon = "trending_up",
            IsAutoAssigned = false,
            IsActive = true,
            SortOrder = 8,
            AllowedRoles = """["Admin","Manager","OfficeManager"]""",
        }, [
            ("leads-overview", true),
            ("leads-walkthrough", true),
            ("leads-field-reference", false),
            ("leads-quiz", true),
            ("customers-overview", true),
            ("customers-walkthrough", false),
            ("quotes-overview", true),
            ("quotes-walkthrough", false),
            ("sales-orders-overview", true),
            ("sales-orders-walkthrough", false),
            ("shipments-overview", true),
            ("shipments-walkthrough", false),
        ]);

        // ── Path 9: Quality Inspector ───────────────────────────────────
        await SeedPath(new TrainingPath
        {
            Title = "Quality Inspector",
            Slug = "quality-inspector",
            Description = "Training for quality staff: inspections and NCRs, lot/serial traceability, and compliance forms.",
            Icon = "fact_check",
            IsAutoAssigned = false,
            IsActive = true,
            SortOrder = 9,
            AllowedRoles = """["Admin","Manager","Engineer"]""",
        }, [
            ("quality-overview", true),
            ("quality-walkthrough", true),
            ("quality-field-reference", false),
            ("quality-quiz", true),
            ("production-lots-overview", true),
            ("production-lots-walkthrough", true),
            ("production-lots-quiz", true),
            ("compliance-forms-overview", true),
            ("compliance-forms-walkthrough", false),
            ("compliance-forms-quiz", true),
        ]);

        // ── Path 10: Accounting / Bookkeeper ────────────────────────────
        await SeedPath(new TrainingPath
        {
            Title = "Accounting / Bookkeeper",
            Slug = "accounting-bookkeeper",
            Description = "Training for finance staff: financial reports, invoices, payments received, and accounts payable.",
            Icon = "account_balance",
            IsAutoAssigned = false,
            IsActive = true,
            SortOrder = 10,
            AllowedRoles = """["Admin","Manager","OfficeManager"]""",
        }, [
            ("accounting-overview", true),
            ("accounting-walkthrough", true),
            ("accounting-field-reference", false),
            ("accounting-quiz", true),
            ("invoices-overview", true),
            ("invoices-walkthrough", true),
            ("invoices-quiz", true),
            ("payments-overview", true),
            ("payments-walkthrough", true),
            ("payments-quiz", true),
            ("payables-overview", true),
            ("payables-walkthrough", true),
            ("payables-quiz", true),
        ]);

        // ── Path 11: Maintenance Technician ─────────────────────────────
        await SeedPath(new TrainingPath
        {
            Title = "Maintenance Technician",
            Slug = "maintenance-technician",
            Description = "Training for maintenance staff: predictive maintenance, asset/tooling records, and PM scheduling.",
            Icon = "handyman",
            IsAutoAssigned = false,
            IsActive = true,
            SortOrder = 11,
            AllowedRoles = """["Admin","Manager","Engineer","ProductionWorker"]""",
        }, [
            ("maintenance-overview", true),
            ("maintenance-walkthrough", true),
            ("maintenance-field-reference", false),
            ("maintenance-quiz", true),
            ("assets-overview", true),
            ("assets-walkthrough", true),
            ("assets-quiz", true),
        ]);

        // ── Path 12: Warehouse / Inventory Clerk ────────────────────────
        await SeedPath(new TrainingPath
        {
            Title = "Warehouse / Inventory Clerk",
            Slug = "warehouse-inventory-clerk",
            Description = "Training for warehouse staff: inventory and bin management, parts catalog, and PO receiving.",
            Icon = "warehouse",
            IsAutoAssigned = false,
            IsActive = true,
            SortOrder = 12,
            AllowedRoles = """["Admin","Manager","OfficeManager","ProductionWorker"]""",
        }, [
            ("inventory-overview", true),
            ("inventory-walkthrough", true),
            ("inventory-field-reference", false),
            ("inventory-quiz", true),
            ("parts-overview", true),
            ("parts-walkthrough", false),
            ("parts-quiz", true),
            ("purchase-orders-overview", true),
            ("purchase-orders-walkthrough", false),
            ("purchase-orders-quiz", true),
        ]);

        // ── Path 13: Shipping & Receiving ───────────────────────────────
        await SeedPath(new TrainingPath
        {
            Title = "Shipping & Receiving",
            Slug = "shipping-receiving",
            Description = "Training for shipping/receiving staff: outbound shipments and carriers, PO receiving, and inventory.",
            Icon = "local_shipping",
            IsAutoAssigned = false,
            IsActive = true,
            SortOrder = 13,
            AllowedRoles = """["Admin","Manager","OfficeManager","ProductionWorker"]""",
        }, [
            ("shipments-overview", true),
            ("shipments-walkthrough", true),
            ("shipments-field-reference", false),
            ("shipments-quiz", true),
            ("purchase-orders-overview", true),
            ("purchase-orders-walkthrough", true),
            ("purchase-orders-quiz", true),
            ("inventory-overview", true),
            ("inventory-walkthrough", false),
        ]);

        Log.Information("Seeded training paths");
    }

    private async Task SeedPath(TrainingPath path, (string slug, bool required)[] modules)
    {
        var existingPath = await _db.TrainingPaths
            .FirstOrDefaultAsync(p => p.Slug == path.Slug);

        if (existingPath != null) return;

        _db.TrainingPaths.Add(path);
        await _db.SaveChangesAsync();

        var position = 1;
        foreach (var (slug, required) in modules)
        {
            var moduleId = Lookup(slug);
            if (moduleId == 0)
            {
                Log.Warning("Training path {Path} references unknown module slug {Slug}", path.Slug, slug);
                continue;
            }

            _db.TrainingPathModules.Add(new TrainingPathModule
            {
                PathId = path.Id,
                ModuleId = moduleId,
                Position = position++,
                IsRequired = required,
            });
        }

        await _db.SaveChangesAsync();
    }
}
