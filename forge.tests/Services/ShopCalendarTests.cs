using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class ShopCalendarTests
{
    [Fact]
    public async Task LoadDefaultAsync_WithoutACalendar_IsMondayToFriday()
    {
        using var db = TestDbContextFactory.Create();

        var calendar = await ShopCalendar.LoadDefaultAsync(db, CancellationToken.None);

        calendar.Should().BeSameAs(ShopCalendar.MondayToFriday);
    }

    [Fact]
    public async Task LoadDefaultAsync_UsesTheDefaultCalendarsDaysAndObservedHolidays()
    {
        using var db = TestDbContextFactory.Create();
        var calendar = new WorkingCalendar { Name = "Plant", IsDefault = true, WorkingDaysMask = 126 };
        calendar.Holidays.Add(new Holiday { Name = "Shutdown", Date = new DateOnly(2026, 10, 10), ObservedDate = new DateOnly(2026, 10, 9) });
        db.WorkingCalendars.AddRange(
            calendar,
            new WorkingCalendar { Name = "Other", WorkingDaysMask = 127 });
        await db.SaveChangesAsync();

        var loaded = await ShopCalendar.LoadDefaultAsync(db, CancellationToken.None);

        loaded.WorkingDaysMask.Should().Be(126);
        loaded.IsWorkingDay(new DateOnly(2026, 10, 9)).Should().BeFalse();
        loaded.IsWorkingDay(new DateOnly(2026, 10, 10)).Should().BeTrue();
        loaded.IsWorkingDay(new DateOnly(2026, 10, 11)).Should().BeFalse();
    }

    [Fact]
    public void CalendarDaysAfter_WithNoWorkingDays_TreatsEveryDayAsWorking()
    {
        new ShopCalendar(0, new HashSet<DateOnly>()).CalendarDaysAfter(new DateOnly(2026, 10, 8), 3).Should().Be(3);
    }
}
