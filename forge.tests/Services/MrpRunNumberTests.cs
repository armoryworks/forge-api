using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class MrpRunNumberTests
{
    [Fact]
    public async Task Run_number_uses_the_shop_local_date_and_time_not_utc()
    {
        using var db = TestDbContextFactory.Create();
        db.WorkingCalendars.Add(new WorkingCalendar { Name = "Plant", TimeZone = "America/Denver", IsDefault = true });
        await db.SaveChangesAsync();
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 20, 3, 15, 30, TimeSpan.Zero));
        var mrp = new MrpService(db, clock.Object, new PartSourcingResolver(db), NullLogger<MrpService>.Instance);

        var run = await mrp.ExecuteRunAsync(new MrpRunOptions());

        run.RunNumber.Should().Be("MRP-20261019-211530");
    }

    [Fact]
    public async Task Run_number_falls_back_to_utc_without_a_shop_time_zone()
    {
        using var db = TestDbContextFactory.Create();
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 20, 3, 15, 30, TimeSpan.Zero));
        var mrp = new MrpService(db, clock.Object, new PartSourcingResolver(db), NullLogger<MrpService>.Instance);

        var run = await mrp.ExecuteRunAsync(new MrpRunOptions());

        run.RunNumber.Should().Be("MRP-20261020-031530");
    }
}
