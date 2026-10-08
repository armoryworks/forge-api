using FluentAssertions;

using Forge.Api.Features.Activity;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Activity;

public class GetEntityHistoryDisplayTests
{
    private const int JobId = 42;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly GetEntityHistoryHandler _handler;
    private readonly DateTimeOffset _now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public GetEntityHistoryDisplayTests()
    {
        _handler = new GetEntityHistoryHandler(_db);
    }

    [Fact]
    public async Task AssigneeChange_ReadsAsNamesWithNoneForEmpty()
    {
        await SeedUserAsync(5, "Admin", "User", "AU");
        await SeedFieldChangeAsync("AssigneeId", "", "5", userId: 5);

        var result = await _handler.Handle(new GetEntityHistoryQuery("Job", JobId), CancellationToken.None);

        var row = result.Should().ContainSingle().Subject;
        row.Description.Should().Be("Assignee: (none) → Admin User");
        row.OldValue.Should().Be("(none)");
        row.NewValue.Should().Be("Admin User");
        row.UserName.Should().Be("Admin User");
        row.UserInitials.Should().Be("AU");
    }

    [Fact]
    public async Task StageAndPartChanges_ResolveToStageNameAndPartNumber()
    {
        var cutting = new JobStage { TrackTypeId = 1, Name = "Cutting", Code = "cut" };
        var welding = new JobStage { TrackTypeId = 1, Name = "Welding", Code = "weld" };
        var part = new Part { PartNumber = "PN-1001", Name = "Bracket" };
        _db.JobStages.AddRange(cutting, welding);
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        await SeedFieldChangeAsync("CurrentStageId", cutting.Id.ToString(), welding.Id.ToString());
        await SeedFieldChangeAsync("PartId", "", part.Id.ToString(), offsetSeconds: 1);

        var result = await _handler.Handle(new GetEntityHistoryQuery("Job", JobId), CancellationToken.None);

        result.Select(r => r.Description).Should().BeEquivalentTo(
            ["Part: (none) → PN-1001", "Stage: Cutting → Welding"],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task UnknownIdField_IsLabelledInWordsAndKeepsItsRawValue()
    {
        await SeedFieldChangeAsync("SalesOrderLineId", "7", "");

        var result = await _handler.Handle(new GetEntityHistoryQuery("Job", JobId), CancellationToken.None);

        result.Should().ContainSingle().Which.Description.Should().Be("Sales Order Line: 7 → (none)");
    }

    [Fact]
    public async Task BoardPositionAndVersionChanges_AreLeftOut()
    {
        await SeedFieldChangeAsync("BoardPosition", "1", "2");
        await SeedFieldChangeAsync("Version", "3", "4");
        await SeedFieldChangeAsync("Title", "Old", "New");

        var result = await _handler.Handle(new GetEntityHistoryQuery("Job", JobId), CancellationToken.None);

        result.Should().ContainSingle().Which.Description.Should().Be("Title: Old → New");
    }

    [Fact]
    public async Task SystemRowsAndCommentsAreHandled()
    {
        _db.ActivityLogs.AddRange(
            new ActivityLog
            {
                EntityType = "Job", EntityId = JobId, Action = "Created",
                Description = "Job created", CreatedAt = _now,
            },
            new ActivityLog
            {
                EntityType = "Job", EntityId = JobId, Action = "Comment",
                Description = "Looks good", CreatedAt = _now,
            });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetEntityHistoryQuery("Job", JobId), CancellationToken.None);

        var row = result.Should().ContainSingle().Subject;
        row.Description.Should().Be("Job created");
        row.UserName.Should().BeNull();
    }

    private async Task SeedUserAsync(int id, string firstName, string lastName, string initials)
    {
        _db.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = $"user{id}@forge.local",
            Email = $"user{id}@forge.local",
            FirstName = firstName,
            LastName = lastName,
            Initials = initials,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedFieldChangeAsync(
        string field, string oldValue, string newValue, int? userId = null, int offsetSeconds = 0)
    {
        _db.ActivityLogs.Add(new ActivityLog
        {
            EntityType = "Job",
            EntityId = JobId,
            UserId = userId,
            Action = "FieldChanged",
            FieldName = field,
            OldValue = oldValue,
            NewValue = newValue,
            Description = $"{field} changed",
            CreatedAt = _now.AddSeconds(offsetSeconds),
        });
        await _db.SaveChangesAsync();
    }
}
