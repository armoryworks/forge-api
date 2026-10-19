using FluentAssertions;

using Forge.Api.Services;

namespace Forge.Tests.Services;

public class MpsConsumptionTests
{
    private static readonly DateTimeOffset Week1 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SalesOrdersInABucket_ConsumeThatBucketsMps()
    {
        var net = MpsConsumption.NetQuantities(
            [(1, 10, 2000m, Week1), (2, 10, 2000m, Week1.AddDays(7))],
            [(10, 29000m, new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero))]);

        net[1].Should().Be(0m);
        net[2].Should().Be(2000m);
    }

    [Fact]
    public void SalesOrdersSmallerThanTheMps_ReduceIt()
    {
        var net = MpsConsumption.NetQuantities(
            [(1, 10, 2000m, Week1)],
            [(10, 500m, Week1.AddDays(-2))]);

        net[1].Should().Be(1500m);
    }

    [Fact]
    public void SalesOrdersAfterTheLastBucket_ConsumeNothing()
    {
        var net = MpsConsumption.NetQuantities(
            [(1, 10, 2000m, Week1)],
            [(10, 500m, Week1.AddDays(3))]);

        net[1].Should().Be(2000m);
    }

    [Fact]
    public void SalesOrderOnTheDueDateLaterInTheDay_IsInsideTheBucket()
    {
        var net = MpsConsumption.NetQuantities(
            [(1, 10, 2000m, Week1)],
            [(10, 800m, Week1.AddHours(18))]);

        net[1].Should().Be(1200m);
    }

    [Fact]
    public void Parts_AreConsumedIndependently()
    {
        var net = MpsConsumption.NetQuantities(
            [(1, 10, 2000m, Week1), (2, 20, 2000m, Week1)],
            [(10, 2500m, Week1)]);

        net[1].Should().Be(0m);
        net[2].Should().Be(2000m);
    }

    [Fact]
    public void TwoLinesOnOneDate_ShareTheBucketsSalesOrders()
    {
        var net = MpsConsumption.NetQuantities(
            [(1, 10, 1000m, Week1), (2, 10, 1000m, Week1)],
            [(10, 1500m, Week1)]);

        net[1].Should().Be(0m);
        net[2].Should().Be(500m);
    }
}
