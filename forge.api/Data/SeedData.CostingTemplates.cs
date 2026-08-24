using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Data.Context;

using Serilog;

namespace Forge.Api.Data;

public static partial class SeedData
{
    /// <summary>
    /// The shipped costing quick-start package — reference data, seeded on every
    /// install (run-once: skipped when any system template exists). Users clone
    /// the idea with their own templates; this one is editable, never deletable.
    /// </summary>
    private static async Task SeedCostingTemplatesAsync(AppDbContext db)
    {
        if (await db.CostingTemplates.AnyAsync(t => t.IsSystem)) return;

        var template = new CostingTemplate
        {
            Name = "Standard manufacturing overhead",
            Description = "Utilities, facilities, employer payroll taxes, benefits, and equipment — "
                        + "the usual indirect costs of a small plant, rated over direct labor hours.",
            IsSystem = true,
            Lines =
            [
                new CostingTemplateLine
                {
                    Code = "UTIL", Name = "Utilities", Behavior = OverheadBehavior.Variable,
                    Driver = OverheadDriver.LaborHour, AmountBasis = CostingAmountBasis.MonthlyAmount,
                    GlAccountNumber = "60100", GlAccountName = "Utilities", SortOrder = 0,
                },
                new CostingTemplateLine
                {
                    Code = "FAC", Name = "Facilities & Rent", Behavior = OverheadBehavior.Fixed,
                    Driver = OverheadDriver.LaborHour, AmountBasis = CostingAmountBasis.MonthlyAmount,
                    GlAccountNumber = "60200", GlAccountName = "Facilities & Rent", SortOrder = 1,
                },
                new CostingTemplateLine
                {
                    Code = "TAX", Name = "Employer Payroll Taxes", Behavior = OverheadBehavior.Variable,
                    Driver = OverheadDriver.LaborHour, AmountBasis = CostingAmountBasis.PercentOfWages,
                    DefaultValue = 7.65m,
                    GlAccountNumber = "60300", GlAccountName = "Payroll Tax Expense", SortOrder = 2,
                },
                new CostingTemplateLine
                {
                    Code = "BEN", Name = "Employee Benefits", Behavior = OverheadBehavior.Variable,
                    Driver = OverheadDriver.LaborHour, AmountBasis = CostingAmountBasis.MonthlyPerEmployee,
                    GlAccountNumber = "60400", GlAccountName = "Employee Benefits", SortOrder = 3,
                },
                new CostingTemplateLine
                {
                    Code = "EQUIP", Name = "Equipment & Depreciation", Behavior = OverheadBehavior.Fixed,
                    Driver = OverheadDriver.LaborHour, AmountBasis = CostingAmountBasis.AnnualAmount,
                    GlAccountNumber = "60500", GlAccountName = "Equipment & Depreciation", SortOrder = 4,
                },
            ],
        };

        db.CostingTemplates.Add(template);
        await db.SaveChangesAsync();
        Log.Information("Seeded the system costing template ({Lines} lines)", template.Lines.Count);
    }
}
