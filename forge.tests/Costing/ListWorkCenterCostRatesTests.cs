using FluentAssertions;

using Forge.Api.Features.Costing.Tier3;
using Forge.Core.Entities;
using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Costing;

public class ListWorkCenterCostRatesTests
{
    [Fact]
    public async Task Handle_ReturnsWorkCenterCodeAndName_OrderedByCode()
    {
        using var db = TestDbContextFactory.Create();
        var press = new WorkCenter { Code = "WC-PRESS", Name = "Press Line" };
        var assembly = new WorkCenter { Code = "WC-ASM", Name = "Assembly Bench" };
        var period = new CostingPeriod { StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), Status = CostingPeriodStatus.Frozen };
        db.AddRange(press, assembly, period);
        await db.SaveChangesAsync();
        db.AddRange(
            new WorkCenterCostRate { WorkCenterId = press.Id, CostingPeriodId = period.Id, LaborRate = 40m },
            new WorkCenterCostRate { WorkCenterId = assembly.Id, CostingPeriodId = period.Id, LaborRate = 32m });
        await db.SaveChangesAsync();

        var rows = await new ListWorkCenterCostRatesHandler(db).Handle(new ListWorkCenterCostRatesQuery(period.Id), CancellationToken.None);

        rows.Select(r => (r.WorkCenterCode, r.WorkCenterName, r.LaborRate)).Should().Equal(
            ("WC-ASM", "Assembly Bench", 32m),
            ("WC-PRESS", "Press Line", 40m));
    }
}
