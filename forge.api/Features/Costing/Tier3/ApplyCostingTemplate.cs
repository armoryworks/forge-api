using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Costing;
using Forge.Data.Context;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>
/// Applies a costing template: each line's answer is annualized per its
/// <see cref="CostingAmountBasis"/> and becomes an overhead pool with a budget
/// (rated over direct labor hours) on the plant cost center for the fiscal-year
/// costing period; lines carrying a GL account also mirror into an expense
/// account + full-year GL budget line when CAP-ACCT-FULLGL is on. Idempotent:
/// re-applying reuses everything by code/number and upserts the amounts.
/// </summary>
[RequiresCapability("CAP-COSTING-TIER3-ABC")]
public record ApplyCostingTemplateCommand(int TemplateId, ApplyCostingTemplateRequestModel Model)
    : IRequest<CostingQuickStartResponseModel>;

public class ApplyCostingTemplateValidator : AbstractValidator<ApplyCostingTemplateCommand>
{
    public ApplyCostingTemplateValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.Model.FiscalYear).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Model.DirectHeadcount).GreaterThan(0);
        RuleFor(x => x.Model.AverageHourlyWage).GreaterThan(0);
        RuleForEach(x => x.Model.Values.Values).GreaterThanOrEqualTo(0)
            .OverridePropertyName("Values")
            .WithMessage("Line values cannot be negative.");
    }
}

public class ApplyCostingTemplateHandler(
    AppDbContext db,
    IMediator mediator,
    ICapabilitySnapshotProvider capabilities)
    : IRequestHandler<ApplyCostingTemplateCommand, CostingQuickStartResponseModel>
{
    private const decimal HoursPerEmployeeYear = 2080m;

    public async Task<CostingQuickStartResponseModel> Handle(ApplyCostingTemplateCommand request, CancellationToken ct)
    {
        var m = request.Model;
        var notes = new List<string>();

        var template = await db.CostingTemplates.AsNoTracking()
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct)
            ?? throw new KeyNotFoundException($"Costing template {request.TemplateId} not found.");
        var lines = template.Lines.OrderBy(l => l.SortOrder).ToList();

        var hours = m.DirectHeadcount * HoursPerEmployeeYear;
        var annualWages = hours * m.AverageHourlyWage;
        var values = new Dictionary<string, decimal>(m.Values, StringComparer.OrdinalIgnoreCase);

        decimal Annualize(CostingTemplateLine line, decimal value) => line.AmountBasis switch
        {
            CostingAmountBasis.AnnualAmount => value,
            CostingAmountBasis.MonthlyAmount => value * 12m,
            CostingAmountBasis.MonthlyPerEmployee => value * 12m * m.DirectHeadcount,
            CostingAmountBasis.PercentOfWages => Math.Round(annualWages * value / 100m, 2),
            _ => value,
        };

        var amounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var value = values.TryGetValue(line.Code, out var v) ? v : line.DefaultValue ?? 0m;
            amounts[line.Code] = Annualize(line, value);
        }

        // ── Plant cost center + fiscal-year costing period (reuse by code / dates) ──
        var center = await db.CostingCostCenters.FirstOrDefaultAsync(c => c.Code == "PLANT", ct);
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

        // ── Overhead pools + budgets (rate derived over direct labor hours) ──
        var configured = new List<string>();
        foreach (var line in lines)
        {
            var amount = amounts[line.Code];
            if (amount <= 0m)
            {
                notes.Add($"{line.Name}: no amount given — pool skipped.");
                continue;
            }

            var existing = await db.OverheadCostPools
                .FirstOrDefaultAsync(p => p.Code == line.Code && p.CostingCostCenterId == centerId, ct);
            var poolId = existing?.Id
                ?? (await mediator.Send(new CreateOverheadPoolCommand(
                    centerId, WorkCenterId: null, line.Code, line.Name, line.Behavior,
                    FixedPortion: null, line.Driver), ct)).Id;

            await mediator.Send(new UpsertOverheadPoolBudgetCommand(poolId, periodId, amount, hours), ct);
            configured.Add(line.Code);
        }

        // ── GL half: expense accounts + full-year budget lines for lines that name one. ──
        var glCreated = false;
        var glLines = lines.Where(l => !string.IsNullOrWhiteSpace(l.GlAccountNumber)).ToList();
        if (m.CreateGlBudgets && glLines.Count > 0)
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
                    foreach (var line in glLines)
                    {
                        var amount = amounts[line.Code];
                        if (amount <= 0m) continue;

                        var account = await db.GlAccounts.FirstOrDefaultAsync(
                            a => a.BookId == book.Id && a.AccountNumber == line.GlAccountNumber, ct);
                        if (account is null)
                        {
                            account = new GlAccount
                            {
                                BookId = book.Id,
                                AccountNumber = line.GlAccountNumber!,
                                Name = line.GlAccountName ?? line.Name,
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

        // ── Optional: default standard labor rates for users that have none. The hourly
        //    burden is the annualized per-employee + percent-of-wages lines over annual
        //    hours, so labor absorption and the burden pools agree on what an hour costs.
        var ratesSet = 0;
        if (m.SetDefaultLaborRates)
        {
            var burdenAnnual = lines
                .Where(l => l.AmountBasis is CostingAmountBasis.MonthlyPerEmployee or CostingAmountBasis.PercentOfWages)
                .Sum(l => amounts[l.Code]);
            var burdenPerHour = hours > 0 ? Math.Round(burdenAnnual / hours, 2) : 0m;
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
                    Notes = $"Costing template '{template.Name}' default (average wage + hourly burden).",
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
