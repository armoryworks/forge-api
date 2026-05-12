using FluentAssertions;

using Forge.Api.Features.Mrp;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mrp;

public class MasterScheduleHandlerTests
{
    [Fact]
    public async Task CreateMasterSchedule_CreatesWithLines()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "MPS-001", Description = "MPS Part", Status = PartStatus.Active };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var handler = new CreateMasterScheduleHandler(db);
        var command = new CreateMasterScheduleCommand(
            "Q2 2026 Schedule",
            "Test schedule",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMonths(3),
            1,
            [new CreateMasterScheduleLineModel(part.Id, 500m, DateTimeOffset.UtcNow.AddMonths(1), "First batch")]
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Q2 2026 Schedule");
        result.Status.Should().Be(MasterScheduleStatus.Draft);
        result.Lines.Should().HaveCount(1);
        result.Lines[0].Quantity.Should().Be(500m);
    }

    [Fact]
    public async Task ActivateMasterSchedule_DraftToActive()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "MPS-002", Description = "Activate Part", Status = PartStatus.Active };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var schedule = new MasterSchedule
        {
            Name = "Activate Test",
            Status = MasterScheduleStatus.Draft,
            PeriodStart = DateTimeOffset.UtcNow,
            PeriodEnd = DateTimeOffset.UtcNow.AddMonths(3),
            CreatedByUserId = 1,
            Lines = [new MasterScheduleLine { PartId = part.Id, Quantity = 100m, DueDate = DateTimeOffset.UtcNow.AddMonths(1) }],
        };
        db.MasterSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var handler = new ActivateMasterScheduleHandler(db);
        var command = new ActivateMasterScheduleCommand(schedule.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be(MasterScheduleStatus.Active);
    }

    [Fact]
    public async Task ActivateMasterSchedule_NonDraft_Throws()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var schedule = new MasterSchedule
        {
            Name = "Already Active",
            Status = MasterScheduleStatus.Active,
            PeriodStart = DateTimeOffset.UtcNow,
            PeriodEnd = DateTimeOffset.UtcNow.AddMonths(3),
            CreatedByUserId = 1,
            Lines = [new MasterScheduleLine { PartId = 1, Quantity = 100m, DueDate = DateTimeOffset.UtcNow.AddMonths(1) }],
        };
        db.MasterSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var handler = new ActivateMasterScheduleHandler(db);

        // Act & Assert
        var act = () => handler.Handle(new ActivateMasterScheduleCommand(schedule.Id), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*draft*");
    }

    [Fact]
    public async Task GetMasterSchedules_ReturnsAll()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        db.MasterSchedules.AddRange(
            new MasterSchedule { Name = "S1", Status = MasterScheduleStatus.Draft, PeriodStart = DateTimeOffset.UtcNow, PeriodEnd = DateTimeOffset.UtcNow.AddMonths(3), CreatedByUserId = 1 },
            new MasterSchedule { Name = "S2", Status = MasterScheduleStatus.Active, PeriodStart = DateTimeOffset.UtcNow, PeriodEnd = DateTimeOffset.UtcNow.AddMonths(3), CreatedByUserId = 1 }
        );
        await db.SaveChangesAsync();

        var handler = new GetMasterSchedulesHandler(db);

        // Act
        var result = await handler.Handle(new GetMasterSchedulesQuery(), CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMasterSchedules_FilterByStatus()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        db.MasterSchedules.AddRange(
            new MasterSchedule { Name = "Draft One", Status = MasterScheduleStatus.Draft, PeriodStart = DateTimeOffset.UtcNow, PeriodEnd = DateTimeOffset.UtcNow.AddMonths(3), CreatedByUserId = 1 },
            new MasterSchedule { Name = "Active One", Status = MasterScheduleStatus.Active, PeriodStart = DateTimeOffset.UtcNow, PeriodEnd = DateTimeOffset.UtcNow.AddMonths(3), CreatedByUserId = 1 }
        );
        await db.SaveChangesAsync();

        var handler = new GetMasterSchedulesHandler(db);

        // Act
        var result = await handler.Handle(new GetMasterSchedulesQuery(MasterScheduleStatus.Draft), CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Draft One");
    }
}
