using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>
/// Prepackaged costing setup: answers a handful of questions (headcount, wages,
/// payroll-tax and benefits burden, utilities, facilities, equipment) and
/// populates everything Tier-3 costing needs to start absorbing overhead —
/// a Plant cost center, a fiscal-year costing period, the standard overhead
/// pools with their annual budgets (rates derived over direct labor hours),
/// and, when full GL is enabled, matching expense accounts + GL budget lines
/// so budget-vs-actual reads out of the box. Idempotent: re-applying reuses
/// everything by code/number and upserts the budget amounts.
/// </summary>
[RequiresCapability("CAP-COSTING-TIER3-ABC")]
public record ApplyCostingQuickStartCommand(ApplyCostingQuickStartRequestModel Model)
    : IRequest<CostingQuickStartResponseModel>;

public sealed record ApplyCostingQuickStartRequestModel
{
    public required int FiscalYear { get; init; }
    /// <summary>Direct (production) employees.</summary>
    public required decimal DirectHeadcount { get; init; }
    /// <summary>Average direct hourly wage.</summary>
    public required decimal AverageHourlyWage { get; init; }
    /// <summary>Employer payroll-tax burden as a percent of wages (FICA + FUTA/SUTA). Default 7.65.</summary>
    public decimal PayrollTaxPercent { get; init; } = 7.65m;
    /// <summary>Benefits cost per employee per month (insurance, retirement match, PTO accrual).</summary>
    public decimal BenefitsMonthlyPerEmployee { get; init; }
    public decimal UtilitiesMonthly { get; init; }
    /// <summary>Rent / building / property costs per month.</summary>
    public decimal FacilitiesMonthly { get; init; }
    /// <summary>Equipment depreciation + maintenance per year.</summary>
    public decimal EquipmentAnnual { get; init; }
    /// <summary>Also create the matching GL expense accounts + budget lines (needs CAP-ACCT-FULLGL).</summary>
    public bool CreateGlBudgets { get; init; } = true;
    /// <summary>Set a standard labor rate (wage + hourly burden) for active users that have none.</summary>
    public bool SetDefaultLaborRates { get; init; }
}

public sealed record CostingQuickStartResponseModel(
    int CostingCostCenterId,
    int CostingPeriodId,
    IReadOnlyList<string> PoolsConfigured,
    decimal AnnualDirectLaborHours,
    decimal TotalAnnualOverhead,
    decimal OverheadRatePerLaborHour,
    bool GlBudgetsCreated,
    int LaborRatesSet,
    IReadOnlyList<string> Notes);

public class ApplyCostingQuickStartValidator : AbstractValidator<ApplyCostingQuickStartCommand>
{
    public ApplyCostingQuickStartValidator()
    {
        RuleFor(x => x.Model.FiscalYear).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Model.DirectHeadcount).GreaterThan(0);
        RuleFor(x => x.Model.AverageHourlyWage).GreaterThan(0);
        RuleFor(x => x.Model.PayrollTaxPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.Model.BenefitsMonthlyPerEmployee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Model.UtilitiesMonthly).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Model.FacilitiesMonthly).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Model.EquipmentAnnual).GreaterThanOrEqualTo(0);
    }
}

public class ApplyCostingQuickStartHandler(
    AppDbContext db,
    IMediator mediator,
    ICapabilitySnapshotProvider capabilities)
    : IRequestHandler<ApplyCostingQuickStartCommand, CostingQuickStartResponseModel>
{
    private const decimal HoursPerEmployeeYear = 2080m;

    // (pool code, pool name, behavior, GL account number, GL account name)
    private static readonly (string Code, string Name, OverheadBehavior Behavior, string Account, string AccountName)[] PoolCatalog =
    [
        ("UTIL", "Utilities", OverheadBehavior.Variable, "60100", "Utilities"),
        ("FAC", "Facilities & Rent", OverheadBehavior.Fixed, "60200", "Facilities & Rent"),
        ("BURDEN", "Labor Burden (payroll taxes + benefits)", OverheadBehavior.Variable, "60300", "Payroll Taxes & Benefits"),
        ("EQUIP", "Equipment & Depreciation", OverheadBehavior.Fixed, "60500", "Equipment & Depreciation"),
    ];

    public async Task<CostingQuickStartResponseModel> Handle(ApplyCostingQuickStartCommand request, CancellationToken ct)
    {
        var m = request.Model;
        var notes = new List<string>();

        var hours = m.DirectHeadcount * HoursPerEmployeeYear;
        var annualWages = hours * m.AverageHourlyWage;
        var amounts = new Dictionary<string, decimal>
        {
            ["UTIL"] = m.UtilitiesMonthly * 12m,
            ["FAC"] = m.FacilitiesMonthly * 12m,
            ["BURDEN"] = Math.Round(annualWages * m.PayrollTaxPercent / 100m
                       + m.BenefitsMonthlyPerEmployee * 12m * m.DirectHeadcount, 2),
            ["EQUIP"] = m.EquipmentAnnual,
        };

        // ── Plant cost center + fiscal-year costing period (reuse by code / dates) ──
        var center = await db.CostingCostCenters
            .FirstOrDefaultAsync(c => c.Code == "PLANT", ct);
        var centerId = center?.Id
            ?? (await mediator.Send(new CreateCostingCostCenterCommand(
                "PLANT", "Plant", CostCenterType.Production,
                ParentId: null, Sqft: null, Headcount: m.DirectHeadcount, IsInventoriable: true), ct)).Id;

        var periodStart = new DateTime(m.FiscalYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(m.FiscalYear, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var period = await db.CostingPeriods
            .FirstOrDefaultAsync(p => p.StartDate == periodStart && p.EndDate == periodEnd, ct);
        var periodId = period?.Id
            ?? (await mediator.Send(new CreateCostingPeriodCommand(periodStart, periodEnd), ct)).Id;

        // ── Overhead pools + annual budgets (rate derived over direct labor hours) ──
        var configured = new List<string>();
        foreach (var pool in PoolCatalog)
        {
            var amount = amounts[pool.Code];
            if (amount <= 0m)
            {
                notes.Add($"{pool.Name}: no amount given — pool skipped.");
                continue;
            }

            var existing = await db.OverheadCostPools
                .FirstOrDefaultAsync(p => p.Code == pool.Code && p.CostingCostCenterId == centerId, ct);
            var poolId = existing?.Id
                ?? (await mediator.Send(new CreateOverheadPoolCommand(
                    centerId, WorkCenterId: null, pool.Code, pool.Name, pool.Behavior,
                    FixedPortion: null, OverheadDriver.LaborHour), ct)).Id;

            await mediator.Send(new UpsertOverheadPoolBudgetCommand(poolId, periodId, amount, hours), ct);
            configured.Add(pool.Code);
        }

        // ── GL half: category expense accounts + full-year budget lines, so the
        //    Accounting → Budgets screen and budget-vs-actual carry the same plan.
        //    Requires FULLGL (the GL is dark without it) and a seeded book.
        var glCreated = false;
        if (m.CreateGlBudgets)
        {
            if (!capabilities.IsEnabled("CAP-ACCT-FULLGL"))
            {
                notes.Add("GL budgets skipped: CAP-ACCT-FULLGL is off — enable it and re-apply to mirror these budgets into the ledger.");
            }
            else
            {
                var book = await db.Books.Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
                if (book is null)
                {
                    notes.Add("GL budgets skipped: no accounting book is seeded on this install.");
                }
                else
                {
                    foreach (var pool in PoolCatalog)
                    {
                        var amount = amounts[pool.Code];
                        if (amount <= 0m) continue;

                        var account = await db.GlAccounts.FirstOrDefaultAsync(
                            a => a.BookId == book.Id && a.AccountNumber == pool.Account, ct);
                        if (account is null)
                        {
                            account = new GlAccount
                            {
                                BookId = book.Id,
                                AccountNumber = pool.Account,
                                Name = pool.AccountName,
                                AccountType = AccountType.Expense,
                                NormalBalance = NormalBalance.Debit,
                                IsPostable = true,
                                IsActive = true,
                            };
                            db.GlAccounts.Add(account);
                            await db.SaveChangesAsync(ct);
                        }

                        var budget = await db.AcctBudgets.FirstOrDefaultAsync(
                            b => b.BookId == book.Id && b.GlAccountId == account.Id
                              && b.FiscalYear == m.FiscalYear && b.PeriodMonth == null, ct);
                        if (budget is null)
                        {
                            db.AcctBudgets.Add(new AcctBudget
                            {
                                BookId = book.Id,
                                GlAccountId = account.Id,
                                FiscalYear = m.FiscalYear,
                                PeriodMonth = null,
                                Amount = amount,
                            });
                        }
                        else
                        {
                            budget.Amount = amount;
                        }
                    }
                    await db.SaveChangesAsync(ct);
                    glCreated = true;
                }
            }
        }

        // ── Optional: default standard labor rates for users that have none.
        //    Standard rate = wage + the hourly burden this plan assumes, so labor
        //    absorption and the burden pool agree on what an hour costs.
        var ratesSet = 0;
        if (m.SetDefaultLaborRates)
        {
            var burdenPerHour = Math.Round(
                m.AverageHourlyWage * m.PayrollTaxPercent / 100m
                + m.BenefitsMonthlyPerEmployee * 12m / HoursPerEmployeeYear, 2);
            var standard = m.AverageHourlyWage + burdenPerHour;

            var usersWithRates = await db.LaborRates
                .Where(r => r.EffectiveTo == null)
                .Select(r => r.UserId)
                .Distinct()
                .ToListAsync(ct);
            var uncovered = await db.Users
                .Where(u => u.IsActive && !usersWithRates.Contains(u.Id))
                .Select(u => u.Id)
                .ToListAsync(ct);
            foreach (var userId in uncovered)
            {
                db.LaborRates.Add(new Forge.Core.Entities.LaborRate
                {
                    UserId = userId,
                    StandardRatePerHour = standard,
                    OvertimeRatePerHour = Math.Round(standard * 1.5m, 2),
                    EffectiveFrom = new DateOnly(m.FiscalYear, 1, 1),
                    Notes = "Costing quick-start default (average wage + hourly burden).",
                });
                ratesSet++;
            }
            if (ratesSet > 0) await db.SaveChangesAsync(ct);
        }

        var totalOverhead = configured.Sum(code => amounts[code]);
        return new CostingQuickStartResponseModel(
            centerId,
            periodId,
            configured,
            hours,
            totalOverhead,
            hours > 0 ? Math.Round(totalOverhead / hours, 2) : 0m,
            glCreated,
            ratesSet,
            notes);
    }
}
