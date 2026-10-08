using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;

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

    [Theory]
    [InlineData(JobOperationStatus.NotStarted, 0, 0, 50)]
    [InlineData(JobOperationStatus.InProgress, 4, 0, 18)]
    [InlineData(JobOperationStatus.InProgress, 4, 2, 12)]
    [InlineData(JobOperationStatus.InProgress, 9, 3, 0)]
    [InlineData(JobOperationStatus.Complete, 4, 0, 0)]
    [InlineData(JobOperationStatus.Skipped, 0, 0, 0)]
    public void RemainingMinutes_CountsFixedTimeUntilStartedAndPiecesLeft(
        JobOperationStatus status, int completed, int scrap, int expected)
    {
        OperationTimeMath.RemainingMinutes(status, 15m, 5m, 3m, 10m, completed, scrap)
            .Should().Be(expected);
    }

    [Fact]
    public void PerPieceRunMinutes_SnapshotOverloadMatchesTheOperationRule()
    {
        var withEach = new Operation { RunMinutesEach = 2m, EstimatedMs = 30000 };
        var msOnly = new Operation { RunMinutesEach = 0m, EstimatedMs = 30000 };

        OperationTimeMath.PerPieceRunMinutes(withEach.RunMinutesEach, withEach.EstimatedMs)
            .Should().Be(OperationTimeMath.PerPieceRunMinutes(withEach));
        OperationTimeMath.PerPieceRunMinutes(msOnly.RunMinutesEach, msOnly.EstimatedMs)
            .Should().Be(OperationTimeMath.PerPieceRunMinutes(msOnly));
    }

    [Fact]
    public void EntryMinutes_ClosedTimerUsesExactTimestamps()
    {
        var start = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

        OperationTimeMath.EntryMinutes(start, start.AddSeconds(90), 2, start.AddHours(5))
            .Should().Be(1.5m);
    }

    [Fact]
    public void EntryMinutes_OpenTimerCountsUpToNow()
    {
        var start = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

        OperationTimeMath.EntryMinutes(start, null, 0, start.AddMinutes(45)).Should().Be(45m);
    }

    [Fact]
    public void EntryMinutes_ManualEntryUsesStoredDuration()
    {
        OperationTimeMath.EntryMinutes(null, null, 30, DateTimeOffset.UnixEpoch).Should().Be(30m);
    }

    [Fact]
    public void EntryMinutes_NeverNegative()
    {
        var start = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

        OperationTimeMath.EntryMinutes(start, null, 0, start.AddMinutes(-5)).Should().Be(0m);
    }

    [Theory]
    [InlineData(2026, 10, 8, 5)]
    [InlineData(2026, 10, 12, 3)]
    public void MakeLeadTimeDays_CountsShopDaysOnTheWorkingCalendar(int year, int month, int day, int expected)
    {
        var routing = new[] { new Operation { RunMinutesEach = 24m } };

        OperationTimeMath.MakeLeadTimeDays(routing, 60m, ShopCalendar.MondayToFriday, new DateOnly(year, month, day))
            .Should().Be(expected);
    }

    [Fact]
    public void MakeLeadTimeDays_SkipsHolidays()
    {
        var routing = new[] { new Operation { RunMinutesEach = 24m } };
        var calendar = new ShopCalendar(WorkCenterCapacity.MondayToFridayMask, new HashSet<DateOnly> { new(2026, 10, 13) });

        OperationTimeMath.MakeLeadTimeDays(routing, 60m, calendar, new DateOnly(2026, 10, 12)).Should().Be(4);
    }

    [Fact]
    public void MakeLeadTimeDays_FollowsTheCalendarsWorkingDays()
    {
        var routing = new[] { new Operation { RunMinutesEach = 24m } };
        var mondayToSaturday = new ShopCalendar(126, new HashSet<DateOnly>());

        OperationTimeMath.MakeLeadTimeDays(routing, 60m, mondayToSaturday, new DateOnly(2026, 10, 8)).Should().Be(4);
    }

    [Fact]
    public void MakeLeadTimeDays_AddsSubcontractTurnTimeInsteadOfItsRunTime()
    {
        var routing = new[]
        {
            new Operation { StepNumber = 10, RunMinutesEach = 8m },
            new Operation { StepNumber = 20, IsSubcontract = true, RunMinutesEach = 30m, SubcontractTurnTimeDays = 2.5m },
            new Operation { StepNumber = 30, IsSubcontract = true },
        };

        OperationTimeMath.MakeLeadTimeDays(routing, 60m, ShopCalendar.MondayToFriday, new DateOnly(2026, 10, 12))
            .Should().Be(4);
    }

    [Fact]
    public void MakeLeadTimeDaysBefore_CountsShopDaysBackOverTheWeekend()
    {
        var routing = new[] { new Operation { RunMinutesEach = 24m } };

        OperationTimeMath.MakeLeadTimeDaysBefore(routing, 60m, ShopCalendar.MondayToFriday, new DateOnly(2026, 10, 12))
            .Should().Be(5);
    }

    [Fact]
    public void MakeLeadTimeDays_IsSevenWithoutARouting()
    {
        OperationTimeMath.MakeLeadTimeDays([], 60m, ShopCalendar.MondayToFriday, new DateOnly(2026, 10, 8))
            .Should().Be(7);
    }
}
