using Bogus;
using FluentAssertions;

using Microsoft.AspNetCore.Identity;

using Moq;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class IdentifyScanHandlerTests
{
    private readonly AppDbContext _db;
    private readonly Mock<UserManager<ApplicationUser>> _userManager;
    private readonly IdentifyScanHandler _handler;
    private readonly Faker _faker = new();

    public IdentifyScanHandlerTests()
    {
        _db = TestDbContextFactory.Create();

        var store = new Mock<IUserStore<ApplicationUser>>();
        _userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _handler = new IdentifyScanHandler(_db, _userManager.Object);
    }

    [Fact]
    public async Task Handle_KnownBarcodeValue_ReturnsMatch()
    {
        // Arrange
        var trackType = new TrackType { Name = "Production", Code = "PROD", IsActive = true };
        _db.TrackTypes.Add(trackType);
        await _db.SaveChangesAsync();

        var stage = new JobStage
        {
            TrackTypeId = trackType.Id,
            Name = "In Production",
            Code = "in_production",
            SortOrder = 1,
            Color = "#22c55e",
            IsActive = true,
        };
        _db.JobStages.Add(stage);
        await _db.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-1001",
            Title = "Test Job",
            TrackTypeId = trackType.Id,
            CurrentStageId = stage.Id,
            Priority = JobPriority.Normal,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var barcode = new Barcode
        {
            Value = "BC-JOB-1001",
            EntityType = BarcodeEntityType.Job,
            JobId = job.Id,
            IsActive = true,
        };
        _db.Barcodes.Add(barcode);
        await _db.SaveChangesAsync();

        var query = new IdentifyScanQuery("BC-JOB-1001");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.ScanType.Should().Be("job");
        result.EntityId.Should().Be(job.Id);
        result.EntityNumber.Should().Be("JOB-1001");
        result.EntityTitle.Should().Be("Test Job");
        result.StageName.Should().Be("In Production");
        result.StageColor.Should().Be("#22c55e");
    }

    [Fact]
    public async Task Handle_UnknownValue_ReturnsNotFound()
    {
        // Arrange
        // UserManager.Users must return a queryable that supports async (EF InMemory does)
        _userManager.Setup(m => m.Users).Returns(_db.Users);

        var query = new IdentifyScanQuery("UNKNOWN-BARCODE-VALUE");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.ScanType.Should().Be("unknown");
        result.EntityId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UserScanIdentifier_ReturnsEmployee()
    {
        // Arrange
        var user = new ApplicationUser
        {
            FirstName = "Daniel",
            LastName = "Hartman",
            UserName = "daniel@example.com",
            Email = "daniel@example.com",
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _db.UserScanIdentifiers.Add(new UserScanIdentifier
        {
            UserId = user.Id,
            IdentifierType = "rfid",
            IdentifierValue = "RFID-12345",
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        var query = new IdentifyScanQuery("RFID-12345");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.ScanType.Should().Be("employee");
        result.EntityId.Should().Be(user.Id);
        result.EntityTitle.Should().Be("Hartman, Daniel");
    }

    [Fact]
    public async Task Handle_LegacyEmployeeBarcode_ReturnsEmployee()
    {
        // Arrange
        var user = new ApplicationUser
        {
            FirstName = "Alice",
            LastName = "Johnson",
            UserName = "alice@example.com",
            Email = "alice@example.com",
            EmployeeBarcode = "EMP-99",
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Mock Users queryable via UserManager
        _userManager.Setup(m => m.Users)
            .Returns(_db.Users);

        var query = new IdentifyScanQuery("EMP-99");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.ScanType.Should().Be("employee");
        result.EntityId.Should().Be(user.Id);
        result.EntityTitle.Should().Be("Johnson, Alice");
    }

    [Fact]
    public async Task Handle_PartBarcode_ReturnsPart()
    {
        // Arrange
        var barcode = new Barcode
        {
            Value = "BC-PART-42",
            EntityType = BarcodeEntityType.Part,
            PartId = 42,
            IsActive = true,
        };
        _db.Barcodes.Add(barcode);
        await _db.SaveChangesAsync();

        var query = new IdentifyScanQuery("BC-PART-42");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.ScanType.Should().Be("part");
        result.EntityId.Should().Be(42);
    }

    [Fact]
    public async Task Handle_WhitespaceAroundValue_TrimsAndMatches()
    {
        // Arrange
        var barcode = new Barcode
        {
            Value = "BC-ASSET-7",
            EntityType = BarcodeEntityType.Asset,
            AssetId = 7,
            IsActive = true,
        };
        _db.Barcodes.Add(barcode);
        await _db.SaveChangesAsync();

        var query = new IdentifyScanQuery("  BC-ASSET-7  ");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.ScanType.Should().Be("asset");
        result.EntityId.Should().Be(7);
    }
}
