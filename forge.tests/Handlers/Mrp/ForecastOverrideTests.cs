using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Mrp;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mrp;

public class ForecastOverrideTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset November = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly ForecastService _forecasts;

    public ForecastOverrideTests()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        _forecasts = new ForecastService(_db, clock.Object);
    }

    [Fact]
    public async Task OverrideOnAZeroBucket_BecomesAnMpsLine()
    {
        var (forecastId, scheduleId) = await SeedApprovedForecastAsync();

        await Override(forecastId, November.ToOffset(TimeSpan.FromHours(-6)), 2500m);
        await Apply(forecastId, scheduleId);

        var line = await _db.MasterScheduleLines.AsNoTracking().SingleAsync(l => l.MasterScheduleId == scheduleId);
        line.Quantity.Should().Be(2500m);
        line.DueDate.Should().Be(new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task LatestOverrideForAPeriod_Wins()
    {
        var (forecastId, scheduleId) = await SeedApprovedForecastAsync();

        await Override(forecastId, November, 2500m);
        await Override(forecastId, November, 1800m);
        await Apply(forecastId, scheduleId);

        (await _db.MasterScheduleLines.AsNoTracking().SingleAsync(l => l.MasterScheduleId == scheduleId)).Quantity.Should().Be(1800m);
    }

    [Fact]
    public async Task OverrideForAnUnknownPeriod_IsRejected()
    {
        var (forecastId, _) = await SeedApprovedForecastAsync();

        var act = () => Override(forecastId, new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero), 10m);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task OverrideForAHistoryPeriod_IsRejected()
    {
        var (forecastId, _) = await SeedApprovedForecastAsync();

        var act = () => Override(forecastId, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), 10m);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*history*");
    }

    [Fact]
    public async Task ForecastHistory_StartsOnTheFirstOfTheOldestMonth()
    {
        var part = await SeedPartAsync();
        await SeedSalesOrderAsync(part, 300m, new DateTimeOffset(2026, 4, 5, 0, 0, 0, TimeSpan.Zero));

        var forecast = await _forecasts.GenerateForecastAsync(part.Id, "Six months", ForecastMethod.MovingAverage, 6, 3, null, null);

        forecast.ForecastBuckets!.Single(b => b.PeriodStart == new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero))
            .HistoricalQuantity.Should().Be(300m);
    }

    private async Task<(int ForecastId, int ScheduleId)> SeedApprovedForecastAsync()
    {
        var part = await SeedPartAsync();
        var forecast = await _forecasts.GenerateForecastAsync(part.Id, "Zero history", ForecastMethod.MovingAverage, 3, 3, null, null);
        var entity = await _db.DemandForecasts.SingleAsync(f => f.Id == forecast.Id);
        entity.Status = ForecastStatus.Approved;
        var schedule = new MasterSchedule { Name = "Draft", Status = MasterScheduleStatus.Draft, PeriodStart = November, PeriodEnd = November.AddMonths(3) };
        _db.MasterSchedules.Add(schedule);
        await _db.SaveChangesAsync();
        return (forecast.Id, schedule.Id);
    }

    private Task Override(int forecastId, DateTimeOffset periodStart, decimal quantity) =>
        new CreateForecastOverrideHandler(_db).Handle(
            new CreateForecastOverrideCommand(forecastId, periodStart, quantity, null, null), CancellationToken.None);

    private Task Apply(int forecastId, int scheduleId) =>
        new ApplyForecastToMpsHandler(_db).Handle(new ApplyForecastToMpsCommand(forecastId, scheduleId), CancellationToken.None);

    private async Task<Part> SeedPartAsync()
    {
        var part = new Part { PartNumber = $"FC-{Guid.NewGuid():N}"[..12], Name = "Forecast part", Status = PartStatus.Active };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task SeedSalesOrderAsync(Part part, decimal quantity, DateTimeOffset createdAt)
    {
        var customer = new Customer { Name = "History customer" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var order = new SalesOrder { OrderNumber = $"SO-{Guid.NewGuid():N}"[..12], CustomerId = customer.Id, Status = SalesOrderStatus.Shipped, CreatedAt = createdAt };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        _db.SalesOrderLines.Add(new SalesOrderLine { SalesOrderId = order.Id, PartId = part.Id, Description = "x", Quantity = quantity, UnitPrice = 1m });
        await _db.SaveChangesAsync();
    }
}
