using FluentAssertions;
using Forge.Api.Services;
using Forge.Core.Entities;

namespace Forge.Tests.Handlers.Jobs.Operations;

public class OperationTimeMathQuantityTests
{
    [Fact]
    public void JobBuildQuantity_PrefersJobPartsOverTheSalesOrderLine()
    {
        var job = new Job
        {
            PartId = 5,
            JobParts = [new JobPart { PartId = 5, Quantity = 12m }],
            SalesOrderLine = new SalesOrderLine { Quantity = 300m },
        };

        OperationTimeMath.JobBuildQuantity(job).Should().Be(12m);
    }

    [Fact]
    public void JobBuildQuantity_FallsBackToTheSalesOrderLineWhenNoJobPartMatches()
    {
        var job = new Job { PartId = 5, SalesOrderLine = new SalesOrderLine { Quantity = 300m } };

        OperationTimeMath.JobBuildQuantity(job).Should().Be(300m);
    }

    [Fact]
    public void JobBuildQuantity_IsOneWhenNothingCarriesAQuantity()
    {
        OperationTimeMath.JobBuildQuantity(new Job { PartId = 5 }).Should().Be(1m);
        OperationTimeMath.JobBuildQuantity(0m, 0m).Should().Be(1m);
    }
}
