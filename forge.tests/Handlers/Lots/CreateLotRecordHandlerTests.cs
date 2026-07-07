using FluentAssertions;
using Moq;

using Forge.Api.Features.Lots;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Lots;

public class CreateLotRecordHandlerTests
{
    [Fact]
    public async Task Handle_WithExplicitLotNumber_UsesProvidedNumber()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "PART-001", Description = "Test Part" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var handler = new CreateLotRecordHandler(db, Mock.Of<IBarcodeService>());
        var data = new CreateLotRecordRequestModel(
            "LOT-CUSTOM-001", part.Id, null, null, null, 500, null, "SUP-123", "Test notes");
        var command = new CreateLotRecordCommand(data);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.LotNumber.Should().Be("LOT-CUSTOM-001");
        result.PartNumber.Should().Be("PART-001");
        result.Quantity.Should().Be(500);
        result.SupplierLotNumber.Should().Be("SUP-123");
        result.Notes.Should().Be("Test notes");
    }

    [Fact]
    public async Task Handle_WithoutLotNumber_GeneratesLotNumber()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "PART-002", Description = "Auto Lot Part" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var handler = new CreateLotRecordHandler(db, Mock.Of<IBarcodeService>());
        var data = new CreateLotRecordRequestModel(
            null, part.Id, null, null, null, 100, null, null, null);
        var command = new CreateLotRecordCommand(data);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.LotNumber.Should().StartWith("LOT-");
        result.LotNumber.Should().EndWith("-001");
    }

    [Fact]
    public async Task Handle_WithOptionalForeignKeys_SetsCorrectly()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "PART-003", Description = "FK Part" };
        db.Parts.Add(part);

        var job = new Job
        {
            Title = "Test Job",
            JobNumber = "JOB-001",
            TrackTypeId = 1,
            CurrentStageId = 1,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var expiration = DateTimeOffset.UtcNow.AddYears(1);
        var handler = new CreateLotRecordHandler(db, Mock.Of<IBarcodeService>());
        var data = new CreateLotRecordRequestModel(
            "LOT-FK-001", part.Id, job.Id, null, null, 250, expiration, null, null);
        var command = new CreateLotRecordCommand(data);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.JobId.Should().Be(job.Id);
        result.JobNumber.Should().Be("JOB-001");
        result.Quantity.Should().Be(250);
        result.ExpirationDate.Should().BeCloseTo(expiration, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_CreatesScannableLotBarcode()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "PART-004", Description = "Barcode Part" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var barcodes = new Mock<IBarcodeService>();
        var handler = new CreateLotRecordHandler(db, barcodes.Object);
        var command = new CreateLotRecordCommand(new CreateLotRecordRequestModel(
            "LOT-BC-001", part.Id, null, null, null, 10, null, null, null));

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert — every lot gets a scannable barcode keyed to its lot number
        barcodes.Verify(b => b.CreateBarcodeAsync(
            BarcodeEntityType.Lot, result.Id, "LOT-BC-001", It.IsAny<CancellationToken>()), Times.Once);
    }
}
