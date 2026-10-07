using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Authorization;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Integration;

[Collection(CapabilityTestCollection.Name)]
public class KioskClockOutTimerTests(CapabilityTestWebApplicationFactory factory)
{
    [Fact]
    public async Task KioskClockOut_ThroughTheRealPipeline_ClosesTheRunningTimerAtThePunchTime()
    {
        var token = $"kiosk-{Guid.NewGuid():N}";
        int userId;
        int entryId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.ReferenceData.AnyAsync(r => r.GroupCode == "clock_event_type" && r.Code == "ClockOut"))
            {
                db.ReferenceData.Add(new ReferenceData
                {
                    GroupCode = "clock_event_type",
                    Code = "ClockOut",
                    Label = "Clock Out",
                    Metadata = """{"statusMapping":"Out","oppositeCode":"ClockIn","category":"work"}""",
                });
            }

            var user = new ApplicationUser
            {
                FirstName = "Kiosk",
                LastName = "Worker",
                UserName = $"{token}@example.com",
                Email = $"{token}@example.com",
                IsActive = true,
            };
            db.Users.Add(user);
            db.KioskTerminals.Add(new KioskTerminal { Name = "Floor", DeviceToken = token, TeamId = 1, IsActive = true });
            await db.SaveChangesAsync();

            var entry = new TimeEntry
            {
                UserId = user.Id,
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                TimerStart = DateTimeOffset.UtcNow.AddMinutes(-45),
                IsManual = false,
            };
            db.TimeEntries.Add(entry);
            await db.SaveChangesAsync();
            userId = user.Id;
            entryId = entry.Id;

            scope.ServiceProvider.GetRequiredService<IClockEventTypeService>().InvalidateCache();
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(KioskTerminalAuthAttribute.HeaderName, token);
        var response = await client.PostAsJsonAsync(
            "/api/v1/display/shop-floor/clock", new ClockInOutRequestModel(userId, "ClockOut"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clockEvent = await db.ClockEvents.AsNoTracking().SingleAsync(e => e.UserId == userId);
            var entry = await db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entryId);
            entry.TimerStop.Should().Be(clockEvent.Timestamp);
            entry.DurationMinutes.Should().Be(45);
            (await db.ActivityLogs.AsNoTracking().AnyAsync(a =>
                a.EntityType == "ClockEvent" && a.EntityId == clockEvent.Id && a.Action == "clock-event-recorded"))
                .Should().BeTrue();
            (await db.ActivityLogs.AsNoTracking().AnyAsync(a =>
                a.EntityType == "TimeEntry" && a.EntityId == entryId && a.Description == "Stopped timer at 45 min (clocked out)"))
                .Should().BeTrue();
        }
    }
}
