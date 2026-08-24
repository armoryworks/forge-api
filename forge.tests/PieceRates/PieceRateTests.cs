using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PieceRates;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.PieceRates;

/// <summary>
/// Piece rates as timelines: setting a rate closes the open row; work resolves
/// the rate as-of its date (mixed-rate weeks pay each day correctly); history
/// never rewrites (backdating over the active row is refused). The weekly
/// compliance sweep compares piece earnings against hours × the jurisdiction
/// minimum (state from work location, federal floor as the fallback and floor).
/// </summary>
public class PieceRateTests
{
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow { get; } = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero); }

    private static async Task<(AppDbContext Db, Part Part)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "W-100", Description = "Widget" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return (db, part);
    }

    private static async Task<ApplicationUser> AddUserAsync(AppDbContext db, string last, int? workLocationId = null)
    {
        var user = new ApplicationUser
        {
            UserName = $"{last}@forge.local", Email = $"{last}@forge.local",
            FirstName = "Pat", LastName = last, IsActive = true, WorkLocationId = workLocationId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task SetRate_ClosesTheOpenRow_AndWorkResolvesAsOfItsDate()
    {
        var (db, part) = await SeedAsync();
        var user = await AddUserAsync(db, "Piece");
        var set = new SetPieceRateHandler(db, new FixedClock());
        var log = new LogPieceWorkHandler(db);

        await set.Handle(new SetPieceRateCommand(part.Id, null, 0.50m, new DateOnly(2026, 8, 1), null), CancellationToken.None);
        // Rate change effective Thursday 8/20.
        await set.Handle(new SetPieceRateCommand(part.Id, null, 0.60m, new DateOnly(2026, 8, 20), null), CancellationToken.None);

        var rows = await db.PieceRates.OrderBy(r => r.EffectiveFrom).ToListAsync();
        rows.Should().HaveCount(2);
        rows[0].EffectiveTo.Should().Be(new DateOnly(2026, 8, 19), "the old row closes the day before the successor");
        rows[1].EffectiveTo.Should().BeNull();

        // Monday's pieces pay at the old rate, Thursday's at the new one.
        var monday = await log.Handle(new LogPieceWorkCommand(user.Id, part.Id, null, new DateOnly(2026, 8, 17), 100m, null), CancellationToken.None);
        var thursday = await log.Handle(new LogPieceWorkCommand(user.Id, part.Id, null, new DateOnly(2026, 8, 20), 100m, null), CancellationToken.None);
        monday.RateSnapshot.Should().Be(0.50m);
        monday.Earnings.Should().Be(50m);
        thursday.RateSnapshot.Should().Be(0.60m);
        thursday.Earnings.Should().Be(60m);
    }

    [Fact]
    public async Task SetRate_BackdatingOverTheActiveRow_IsRefused()
    {
        var (db, part) = await SeedAsync();
        var set = new SetPieceRateHandler(db, new FixedClock());
        await set.Handle(new SetPieceRateCommand(part.Id, null, 0.50m, new DateOnly(2026, 8, 10), null), CancellationToken.None);

        var act = () => set.Handle(new SetPieceRateCommand(part.Id, null, 0.55m, new DateOnly(2026, 8, 10), null), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*History is never rewritten*");
    }

    [Fact]
    public async Task LogWork_OperationRateWins_PartLevelFallsBack()
    {
        var (db, part) = await SeedAsync();
        var user = await AddUserAsync(db, "Ops");
        var set = new SetPieceRateHandler(db, new FixedClock());
        var log = new LogPieceWorkHandler(db);
        await set.Handle(new SetPieceRateCommand(part.Id, null, 0.50m, new DateOnly(2026, 1, 1), null), CancellationToken.None);

        // No operation rate exists → op-tagged work falls back to the part-level rate.
        var fallback = await log.Handle(new LogPieceWorkCommand(user.Id, part.Id, 999, new DateOnly(2026, 8, 17), 10m, null), CancellationToken.None);
        fallback.RateSnapshot.Should().Be(0.50m);
    }

    [Fact]
    public async Task Compliance_ComputesMakeup_ByWorkLocationState()
    {
        var (db, part) = await SeedAsync();
        db.CompanyLocations.AddRange(
            new CompanyLocation { Name = "HQ", State = "UT", IsDefault = true, IsActive = true },
            new CompanyLocation { Name = "CA plant", State = "CA", IsDefault = false, IsActive = true });
        await db.SaveChangesAsync();
        var caLocation = await db.CompanyLocations.SingleAsync(l => l.State == "CA");

        db.MinimumWageRates.AddRange(
            new MinimumWageRate { StateCode = null, RatePerHour = 7.25m, EffectiveFrom = new DateOnly(2009, 7, 24) },
            new MinimumWageRate { StateCode = "CA", RatePerHour = 16.50m, EffectiveFrom = new DateOnly(2025, 1, 1) });

        var slow = await AddUserAsync(db, "Slow", caLocation.Id);   // CA — high floor, will need make-up
        var fast = await AddUserAsync(db, "Fast");                  // default location UT → no state row → federal

        var week = new DateOnly(2026, 8, 17); // Monday
        var set = new SetPieceRateHandler(db, new FixedClock());
        var log = new LogPieceWorkHandler(db);
        await set.Handle(new SetPieceRateCommand(part.Id, null, 1.00m, new DateOnly(2026, 1, 1), null), CancellationToken.None);
        await log.Handle(new LogPieceWorkCommand(slow.Id, part.Id, null, week, 300m, null), CancellationToken.None);  // $300
        await log.Handle(new LogPieceWorkCommand(fast.Id, part.Id, null, week, 500m, null), CancellationToken.None);  // $500

        // 40 hours each.
        foreach (var u in new[] { slow.Id, fast.Id })
        {
            for (var i = 0; i < 5; i++)
            {
                db.TimeEntries.Add(new TimeEntry { UserId = u, Date = week.AddDays(i), DurationMinutes = 480 });
            }
        }
        await db.SaveChangesAsync();

        var report = await new GetPieceRateComplianceHandler(db)
            .Handle(new GetPieceRateComplianceQuery(week), CancellationToken.None);

        report.Rows.Should().HaveCount(2);
        var slowRow = report.Rows.Single(r => r.UserId == slow.Id);
        slowRow.StateCode.Should().Be("CA");
        slowRow.MinimumWage.Should().Be(16.50m);
        slowRow.RequiredFloor.Should().Be(660m);    // 40 × 16.50
        slowRow.MakeupOwed.Should().Be(360m);       // 660 − 300
        slowRow.EffectiveHourly.Should().Be(7.50m);

        var fastRow = report.Rows.Single(r => r.UserId == fast.Id);
        fastRow.MinimumWage.Should().Be(7.25m, "UT has no state row — federal floor applies");
        fastRow.MakeupOwed.Should().Be(0m, "500 earned beats 40 × 7.25 = 290");

        report.TotalMakeupOwed.Should().Be(360m);
    }
}
