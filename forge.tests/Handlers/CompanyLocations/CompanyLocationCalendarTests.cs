using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.CompanyLocations;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CompanyLocations;

public class CompanyLocationCalendarTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    [Fact]
    public async Task CreateAndUpdate_RoundTripTheWorkingCalendar()
    {
        var twoShift = await AddCalendarAsync("Two shift");
        var weekend = await AddCalendarAsync("Weekend crew");

        var created = await new CreateCompanyLocationHandler(_db).Handle(Create(twoShift.Id), CancellationToken.None);
        created.WorkingCalendarId.Should().Be(twoShift.Id);

        var updated = await new UpdateCompanyLocationHandler(_db).Handle(Update(created.Id, weekend.Id), CancellationToken.None);
        updated.WorkingCalendarId.Should().Be(weekend.Id);

        var listed = await new GetCompanyLocationsHandler(_db).Handle(new GetCompanyLocationsQuery(), CancellationToken.None);
        listed.Single().WorkingCalendarId.Should().Be(weekend.Id);
        (await _db.CompanyLocations.AsNoTracking().SingleAsync()).WorkingCalendarId.Should().Be(weekend.Id);

        var cleared = await new UpdateCompanyLocationHandler(_db).Handle(Update(created.Id, null), CancellationToken.None);
        cleared.WorkingCalendarId.Should().BeNull();
    }

    [Fact]
    public async Task InactiveCalendar_CannotBeAssigned()
    {
        var retired = await AddCalendarAsync("Retired", isActive: false);

        var act = () => new CreateCompanyLocationHandler(_db).Handle(Create(retired.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UnknownCalendar_IsNotFound()
    {
        var act = () => new CreateCompanyLocationHandler(_db).Handle(Create(999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private static CreateCompanyLocationCommand Create(int? calendarId) =>
        new("Plant 1", "1 Main St", null, "Ogden", "UT", "84401", "US", null, true, calendarId);

    private static UpdateCompanyLocationCommand Update(int id, int? calendarId) =>
        new(id, "Plant 1", "1 Main St", null, "Ogden", "UT", "84401", "US", null, true, calendarId);

    private async Task<WorkingCalendar> AddCalendarAsync(string name, bool isActive = true)
    {
        var calendar = new WorkingCalendar { Name = name, TimeZone = "America/Denver", IsActive = isActive };
        _db.WorkingCalendars.Add(calendar);
        await _db.SaveChangesAsync();
        return calendar;
    }
}
