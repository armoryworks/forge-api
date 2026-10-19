using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Repositories;

public class MaintenanceReportTests
{
    [Fact]
    public async Task Maintenance_report_lists_breakdown_downtime_with_mtbf()
    {
        using var db = TestDbContextFactory.Create();
        var press = new Asset { Name = "Injection Press 220T", AssetType = AssetType.Machine };
        db.Assets.Add(press);
        await db.SaveChangesAsync();
        var start = DateTimeOffset.UtcNow.AddDays(-7);
        var end = DateTimeOffset.UtcNow.AddDays(1);
        var leakStart = DateTimeOffset.UtcNow.AddHours(-5);
        db.DowntimeLogs.AddRange(
            new DowntimeLog { AssetId = press.Id, StartedAt = leakStart, EndedAt = leakStart.AddHours(3), Reason = "Hydraulic leak", IsPlanned = false },
            new DowntimeLog { AssetId = press.Id, StartedAt = leakStart.AddDays(-2), EndedAt = leakStart.AddDays(-2).AddHours(1), Reason = "Mold change", IsPlanned = true },
            new DowntimeLog { AssetId = press.Id, StartedAt = start.AddDays(-30), EndedAt = start.AddDays(-29), Reason = "Old outage", IsPlanned = false });
        await db.SaveChangesAsync();

        var report = await new ReportRepository(db).GetMaintenanceAsync(start, end, CancellationToken.None);

        var row = report.Should().ContainSingle().Subject;
        row.AssetName.Should().Be("Injection Press 220T");
        row.ScheduledCount.Should().Be(0);
        row.DowntimeEvents.Should().Be(2);
        row.FailureCount.Should().Be(1);
        row.DowntimeHours.Should().BeApproximately(4m, 0.01m);
        row.UnplannedDowntimeHours.Should().BeApproximately(3m, 0.01m);
        row.MtbfHours.Should().BeApproximately(7m * 24m - 3m, 0.1m);
    }
}
