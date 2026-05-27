using System.Net;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.TimeTracking;

/// <summary>
/// Region 3 · Time Tracking RED test (see ../README.md). Finding TT-01: DELETE/PATCH on a
/// time entry has no ownership check (IDOR) — a non-owner can delete someone else's entry.
/// This seeds an entry for user 2 and deletes it as a different non-manager user. CAP-HR-TIMETRACK
/// is on. TT-04 (GET /entries leaks all users' entries) is tracked in the catalog (list-scoping
/// assertion).
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class TimeTrackingRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public TimeTrackingRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role, string userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", userId);
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    [Fact(Skip = "RED: TT-01 — deleting a time entry has no ownership check (IDOR). " +
                 "Remove Skip when a non-owner deleting another user's entry is rejected (403).")]
    public async Task A_non_owner_cannot_delete_another_users_time_entry()
    {
        int entryId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = new TimeEntry
            {
                UserId = 2,
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                DurationMinutes = 60,
                EntryType = TimeEntryType.Run,
            };
            db.TimeEntries.Add(entry);
            await db.SaveChangesAsync();
            entryId = entry.Id;
        }

        var response = await AuthClient("ProductionWorker", userId: "3")
            .DeleteAsync($"/api/v1/time-tracking/entries/{entryId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "only the owning user (or a manager) may delete a time entry");
    }
}
