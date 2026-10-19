using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public sealed class ProjectRollup
{
    private readonly Dictionary<int, (decimal Labor, decimal Material, decimal Other)> _actuals;
    private const int RootKey = 0;

    private readonly Dictionary<int, List<WbsElement>> _children;

    private ProjectRollup(
        Project project,
        List<WbsElement> elements,
        Dictionary<int, (decimal Labor, decimal Material, decimal Other)> actuals,
        DateOnly today)
    {
        Project = project;
        Elements = elements;
        _actuals = actuals;
        Today = today;

        var ids = elements.Select(e => e.Id).ToHashSet();
        _children = elements
            .GroupBy(e => e.ParentElementId is int parent && ids.Contains(parent) ? parent : RootKey)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.SortOrder).ThenBy(e => e.Code).ToList());
    }

    public Project Project { get; }
    public List<WbsElement> Elements { get; }
    public DateOnly Today { get; }

    public static async Task<ProjectRollup> LoadAsync(AppDbContext db, IClock clock, int projectId, bool track, CancellationToken ct)
    {
        var projects = track ? db.Projects : db.Projects.AsNoTracking();
        var project = await projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new KeyNotFoundException($"Project {projectId} not found");

        var elementsQuery = track ? db.WbsElements : db.WbsElements.AsNoTracking();
        var elements = await elementsQuery.Where(e => e.ProjectId == projectId).ToListAsync(ct);
        var elementIds = elements.Select(e => e.Id).ToList();

        var sums = await db.WbsCostEntries
            .AsNoTracking()
            .Where(c => elementIds.Contains(c.WbsElementId))
            .GroupBy(c => new { c.WbsElementId, c.Category })
            .Select(g => new { g.Key.WbsElementId, g.Key.Category, Amount = g.Sum(c => c.Amount) })
            .ToListAsync(ct);

        var actuals = elementIds.ToDictionary(
            id => id,
            id =>
            {
                var rows = sums.Where(s => s.WbsElementId == id).ToList();
                var labor = rows.Where(r => r.Category == WbsCostCategory.Labor).Sum(r => r.Amount);
                var material = rows.Where(r => r.Category == WbsCostCategory.Material).Sum(r => r.Amount);
                var other = rows.Where(r => r.Category != WbsCostCategory.Labor && r.Category != WbsCostCategory.Material).Sum(r => r.Amount);
                return (labor, material, other);
            });

        var timeZone = await ClockStateRules.ShopTimeZoneAsync(db, ct);
        var today = DateOnly.FromDateTime(ClockStateRules.LocalToday(timeZone, clock.UtcNow));

        return new ProjectRollup(project, elements, actuals, today);
    }

    public (decimal Labor, decimal Material, decimal Other) ActualsFor(int elementId) =>
        _actuals.GetValueOrDefault(elementId);

    public decimal ActualTotal => _actuals.Values.Sum(a => a.Labor + a.Material + a.Other);

    public decimal WbsBudgetTotal => Elements.Sum(e => e.BudgetTotal);

    public decimal BudgetAtCompletion => Project.BudgetTotal > 0 ? Project.BudgetTotal : WbsBudgetTotal;

    public decimal PercentComplete
    {
        get
        {
            var weighted = Weighted(Leaves(), e => e.PercentComplete);
            return weighted ?? Project.PercentComplete ?? 0m;
        }
    }

    public decimal PlannedFraction
    {
        get
        {
            var leaves = Leaves();
            var weighted = Weighted(leaves, e => TimeFraction(e.PlannedStart ?? Project.PlannedStartDate, e.PlannedEnd ?? Project.PlannedEndDate) * 100m);
            return (weighted ?? TimeFraction(Project.PlannedStartDate, Project.PlannedEndDate) * 100m) / 100m;
        }
    }

    public EarnedValueMetricsResponseModel EarnedValue()
    {
        var bac = BudgetAtCompletion;
        var ev = Round(bac * PercentComplete / 100m);
        var pv = Round(bac * PlannedFraction);
        var ac = Round(ActualTotal);
        var cpi = ac > 0 ? ev / ac : 1m;
        var spi = pv > 0 ? ev / pv : 1m;
        var eac = ev > 0 && ac > 0 ? Round(bac / cpi) : Round(ac + (bac - ev));

        return new EarnedValueMetricsResponseModel
        {
            BudgetedCostOfWorkScheduled = pv,
            BudgetedCostOfWorkPerformed = ev,
            ActualCostOfWorkPerformed = ac,
            ScheduleVariance = ev - pv,
            CostVariance = ev - ac,
            SchedulePerformanceIndex = Math.Round(spi, 4),
            CostPerformanceIndex = Math.Round(cpi, 4),
            EstimateAtCompletion = eac,
            EstimateToComplete = Math.Max(0m, eac - ac),
        };
    }

    public ProjectSummaryResponseModel Summary()
    {
        var ev = EarnedValue();
        return new ProjectSummaryResponseModel
        {
            ProjectId = Project.Id,
            BudgetTotal = BudgetAtCompletion,
            ActualTotal = ev.ActualCostOfWorkPerformed,
            CommittedTotal = Project.CommittedTotal,
            EstimateAtCompletion = ev.EstimateAtCompletion,
            VarianceAtCompletion = BudgetAtCompletion - ev.EstimateAtCompletion,
            PercentComplete = Math.Round(PercentComplete, 2),
            WbsTree = Tree(RootKey),
        };
    }

    private IReadOnlyList<WbsElementSummaryResponseModel> Tree(int parentId) =>
        _children.TryGetValue(parentId, out var nodes)
            ? nodes.Select(Node).ToList()
            : [];

    private WbsElementSummaryResponseModel Node(WbsElement element)
    {
        var children = Tree(element.Id);
        var own = ActualsFor(element.Id);
        var budget = element.BudgetTotal + children.Sum(c => c.BudgetTotal);
        var actual = own.Labor + own.Material + own.Other + children.Sum(c => c.ActualTotal);
        var percent = children.Count == 0
            ? element.PercentComplete
            : Weighted(Descendants(element.Id), e => e.PercentComplete) ?? element.PercentComplete;

        return new WbsElementSummaryResponseModel
        {
            Id = element.Id,
            ParentElementId = element.ParentElementId,
            Code = element.Code,
            Name = element.Name,
            Type = element.Type.ToString(),
            BudgetTotal = budget,
            ActualTotal = actual,
            Variance = budget - actual,
            PercentComplete = percent is null ? null : Math.Round(percent.Value, 2),
            Children = children,
        };
    }

    private List<WbsElement> Leaves() =>
        Elements.Where(e => !_children.ContainsKey(e.Id)).ToList();

    private List<WbsElement> Descendants(int elementId)
    {
        var result = new List<WbsElement>();
        if (!_children.TryGetValue(elementId, out var nodes))
            return result;
        foreach (var node in nodes)
        {
            if (_children.ContainsKey(node.Id))
                result.AddRange(Descendants(node.Id));
            else
                result.Add(node);
        }
        return result;
    }

    private static decimal? Weighted(IReadOnlyCollection<WbsElement> elements, Func<WbsElement, decimal?> value)
    {
        var rated = elements.Select(e => (e.BudgetTotal, Value: value(e))).Where(x => x.Value.HasValue).ToList();
        if (elements.Count == 0 || rated.Count == 0)
            return null;

        var totalBudget = elements.Sum(e => e.BudgetTotal);
        if (totalBudget > 0)
            return rated.Sum(x => x.BudgetTotal * Math.Clamp(x.Value!.Value, 0m, 100m)) / totalBudget;

        return elements.Sum(e => Math.Clamp(value(e) ?? 0m, 0m, 100m)) / elements.Count;
    }

    private decimal TimeFraction(DateOnly? start, DateOnly? end)
    {
        if (start is null || end is null)
            return 0m;
        if (Today < start.Value)
            return 0m;
        if (Today >= end.Value || end.Value <= start.Value)
            return 1m;
        var span = end.Value.DayNumber - start.Value.DayNumber;
        return (decimal)(Today.DayNumber - start.Value.DayNumber) / span;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
