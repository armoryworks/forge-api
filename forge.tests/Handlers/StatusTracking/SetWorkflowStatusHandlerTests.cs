using Bogus;
using FluentAssertions;
using Moq;

using Forge.Api.Features.StatusTracking;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.StatusTracking;

public class SetWorkflowStatusHandlerTests
{
    private readonly Mock<IStatusEntryRepository> _statusRepo = new();
    private readonly AppDbContext _dbContext;
    private readonly SetWorkflowStatusHandler _handler;

    private readonly Faker _faker = new();

    public SetWorkflowStatusHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _handler = new SetWorkflowStatusHandler(
            _dbContext,
            _statusRepo.Object,
            Mock.Of<IActivityLogRepository>(),
            Mock.Of<IWorkCenterContext>(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>());
    }

    [Fact]
    public async Task Handle_NoExistingStatus_CreatesNewWorkflowEntry()
    {
        // Arrange
        var entityType = "job";
        var entityId = _faker.Random.Int(1, 100);
        var statusCode = "in_production";

        var expectedResponse = BuildStatusEntryResponse(1, entityType, entityId, statusCode);
        _statusRepo.Setup(r => r.GetHistoryAsync(entityType, entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expectedResponse]);

        var command = new SetWorkflowStatusCommand(entityType, entityId,
            new SetStatusRequestModel(statusCode, "Started production"));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.StatusCode.Should().Be(statusCode);
        result.EntityId.Should().Be(entityId);

        var entries = _dbContext.StatusEntries.ToList();
        entries.Should().HaveCount(1);
        entries[0].StatusCode.Should().Be(statusCode);
        entries[0].Category.Should().Be("workflow");
        entries[0].EndedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ExistingActiveStatus_ClosesItBeforeCreatingNew()
    {
        // Arrange
        var entityType = "job";
        var entityId = 5;

        // Seed an existing active workflow status
        var existingEntry = new StatusEntry
        {
            EntityType = entityType,
            EntityId = entityId,
            StatusCode = "quote_requested",
            StatusLabel = "Quote Requested",
            Category = "workflow",
            StartedAt = DateTime.UtcNow.AddDays(-3),
            EndedAt = null,
        };
        _dbContext.StatusEntries.Add(existingEntry);
        await _dbContext.SaveChangesAsync();

        var newStatusCode = "order_confirmed";
        var expectedResponse = BuildStatusEntryResponse(2, entityType, entityId, newStatusCode);
        _statusRepo.Setup(r => r.GetHistoryAsync(entityType, entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expectedResponse, BuildStatusEntryResponse(1, entityType, entityId, "quote_requested")]);

        var command = new SetWorkflowStatusCommand(entityType, entityId,
            new SetStatusRequestModel(newStatusCode, null));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — the old entry should now have an EndedAt
        var entries = _dbContext.StatusEntries.ToList();
        var closedEntry = entries.First(e => e.StatusCode == "quote_requested");
        closedEntry.EndedAt.Should().NotBeNull();
        closedEntry.EndedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_MultipleActiveStatuses_ClosesAllExistingWorkflowEntries()
    {
        // Arrange
        var entityType = "asset";
        var entityId = 10;

        // Seed two active workflow entries (shouldn't happen normally but must be handled gracefully)
        _dbContext.StatusEntries.AddRange(
            new StatusEntry { EntityType = entityType, EntityId = entityId, StatusCode = "active", StatusLabel = "Active", Category = "workflow", StartedAt = DateTime.UtcNow.AddDays(-5), EndedAt = null },
            new StatusEntry { EntityType = entityType, EntityId = entityId, StatusCode = "maintenance", StatusLabel = "Maintenance", Category = "workflow", StartedAt = DateTime.UtcNow.AddDays(-1), EndedAt = null }
        );
        await _dbContext.SaveChangesAsync();

        var newStatusCode = "offline";
        var expectedResponse = BuildStatusEntryResponse(3, entityType, entityId, newStatusCode);
        _statusRepo.Setup(r => r.GetHistoryAsync(entityType, entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expectedResponse]);

        var command = new SetWorkflowStatusCommand(entityType, entityId,
            new SetStatusRequestModel(newStatusCode, null));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — both existing entries are closed
        var allEntries = _dbContext.StatusEntries.ToList();
        var closedEntries = allEntries.Where(e => e.StatusCode != newStatusCode && e.Category == "workflow").ToList();
        closedEntries.Should().AllSatisfy(e => e.EndedAt.Should().NotBeNull());
    }

    [Fact]
    public async Task Handle_UsesReferenceLabelWhenStatusCodeFound()
    {
        // Arrange
        var entityType = "job";
        var entityId = 20;
        var statusCode = "in_production";
        var configuredLabel = "In Production";

        // Seed reference data entry
        _dbContext.ReferenceData.Add(new ReferenceData
        {
            Code = statusCode,
            Label = configuredLabel,
            GroupCode = "job_status",
            IsActive = true,
        });
        await _dbContext.SaveChangesAsync();

        var expectedResponse = BuildStatusEntryResponse(1, entityType, entityId, statusCode, configuredLabel);
        _statusRepo.Setup(r => r.GetHistoryAsync(entityType, entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expectedResponse]);

        var command = new SetWorkflowStatusCommand(entityType, entityId,
            new SetStatusRequestModel(statusCode, null));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — the entry was saved with the configured label
        var entry = _dbContext.StatusEntries.First(e => e.StatusCode == statusCode);
        entry.StatusLabel.Should().Be(configuredLabel);
    }

    [Fact]
    public async Task Handle_NoReferenceLabelFound_FallsBackToStatusCode()
    {
        // Arrange
        var entityType = "job";
        var entityId = 30;
        var statusCode = "custom_status_with_no_label";

        var expectedResponse = BuildStatusEntryResponse(1, entityType, entityId, statusCode);
        _statusRepo.Setup(r => r.GetHistoryAsync(entityType, entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expectedResponse]);

        var command = new SetWorkflowStatusCommand(entityType, entityId,
            new SetStatusRequestModel(statusCode, null));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — status label falls back to the code itself
        var entry = _dbContext.StatusEntries.First(e => e.EntityId == entityId);
        entry.StatusLabel.Should().Be(statusCode);
    }

    [Fact]
    public async Task Handle_NotesAreTrimmed()
    {
        // Arrange
        var entityType = "job";
        var entityId = 40;
        var statusCode = "review";

        _statusRepo.Setup(r => r.GetHistoryAsync(entityType, entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([BuildStatusEntryResponse(1, entityType, entityId, statusCode)]);

        var command = new SetWorkflowStatusCommand(entityType, entityId,
            new SetStatusRequestModel(statusCode, "  needs review   "));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var entry = _dbContext.StatusEntries.First();
        entry.Notes.Should().Be("needs review");
    }

    [Fact]
    public async Task Handle_HoldCategoryEntries_AreNotClosed()
    {
        // Arrange
        var entityType = "job";
        var entityId = 50;

        // Seed a "hold" category entry (different category — must not be closed)
        var holdEntry = new StatusEntry
        {
            EntityType = entityType,
            EntityId = entityId,
            StatusCode = "pending_material",
            StatusLabel = "Pending Material",
            Category = "hold",
            StartedAt = DateTime.UtcNow.AddHours(-2),
            EndedAt = null,
        };
        _dbContext.StatusEntries.Add(holdEntry);
        await _dbContext.SaveChangesAsync();

        _statusRepo.Setup(r => r.GetHistoryAsync(entityType, entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([BuildStatusEntryResponse(2, entityType, entityId, "in_production")]);

        var command = new SetWorkflowStatusCommand(entityType, entityId,
            new SetStatusRequestModel("in_production", null));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — the hold entry must remain open
        var reloadedHold = _dbContext.StatusEntries.First(e => e.Category == "hold");
        reloadedHold.EndedAt.Should().BeNull();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static StatusEntryResponseModel BuildStatusEntryResponse(
        int id,
        string entityType,
        int entityId,
        string statusCode,
        string? statusLabel = null) =>
        new(id, entityType, entityId, statusCode, statusLabel ?? statusCode, "workflow",
            DateTime.UtcNow, null, null, null, null, DateTime.UtcNow);
}
