using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Entities;

namespace Forge.Tests.Services;

public class OperationTimeMathTests
{
    [Fact]
    public void PerPieceRunMinutes_PrefersRunMinutesEach()
    {
        var op = new Operation { RunMinutesEach = 2m, EstimatedMs = 30000 };

        OperationTimeMath.PerPieceRunMinutes(op).Should().Be(2m);
    }

    [Fact]
    public void PerPieceRunMinutes_FallsBackToEstimatedMs()
    {
        var op = new Operation { RunMinutesEach = 0m, EstimatedMs = 30000 };

        OperationTimeMath.PerPieceRunMinutes(op).Should().Be(0.5m);
    }

    [Fact]
    public void PerPieceRunMinutes_NoTimesEntered_IsZero()
    {
        OperationTimeMath.PerPieceRunMinutes(new Operation()).Should().Be(0m);
    }

    [Fact]
    public void PlannedMinutes_AddsSetupLotAndPerPieceTimesQuantity()
    {
        var op = new Operation { SetupMinutes = 15m, RunMinutesLot = 5m, EstimatedMs = 30000 };

        OperationTimeMath.PlannedMinutes(op, 500m).Should().Be(270m);
    }

    [Fact]
    public void JobBuildQuantity_SumsJobPartsForTheJobsPart()
    {
        var job = new Job
        {
            PartId = 7,
            JobParts =
            [
                new JobPart { PartId = 7, Quantity = 300m },
                new JobPart { PartId = 7, Quantity = 200m },
                new JobPart { PartId = 9, Quantity = 40m },
            ],
        };

        OperationTimeMath.JobBuildQuantity(job).Should().Be(500m);
    }

    [Fact]
    public void JobBuildQuantity_NoMatchingJobParts_IsOne()
    {
        var job = new Job
        {
            PartId = 7,
            JobParts = [new JobPart { PartId = 9, Quantity = 40m }],
        };

        OperationTimeMath.JobBuildQuantity(job).Should().Be(1m);
    }
}
