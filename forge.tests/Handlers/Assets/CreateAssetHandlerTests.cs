using Bogus;
using FluentAssertions;
using Moq;
using Forge.Api.Features.Assets;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Assets;

public class CreateAssetHandlerTests
{
    private readonly Mock<IAssetRepository> _assetRepo = new();
    private readonly CreateAssetHandler _handler;

    private readonly Faker _faker = new();

    public CreateAssetHandlerTests()
    {
        _handler = new CreateAssetHandler(_assetRepo.Object, Mock.Of<IBarcodeService>());
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesAssetAndReturnsResponse()
    {
        // Arrange
        var name = _faker.Commerce.ProductName();
        var location = _faker.Address.BuildingNumber();
        var manufacturer = _faker.Company.CompanyName();
        var model = _faker.Commerce.ProductAdjective();
        var serialNumber = _faker.Random.AlphaNumeric(10).ToUpper();

        var data = new CreateAssetRequestModel(
            name, AssetType.Machine, location, manufacturer, model,
            serialNumber, "Test asset", null, null, null, null, null);

        var expectedResponse = new AssetResponseModel(
            1, name, AssetType.Machine, location, manufacturer, model,
            serialNumber, AssetStatus.Active, null, 0, null,
            false, null, null, 0, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);

        _assetRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var command = new CreateAssetCommand(data);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be(name);

        _assetRepo.Verify(r => r.AddAsync(It.Is<Asset>(a =>
            a.Name == name &&
            a.AssetType == AssetType.Machine &&
            a.Location == location &&
            a.Manufacturer == manufacturer &&
            a.Model == model &&
            a.SerialNumber == serialNumber
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TrimsWhitespace()
    {
        // Arrange
        var data = new CreateAssetRequestModel(
            "  CNC Mill  ", AssetType.Machine, "  Shop Floor  ", "  Haas  ",
            "  VF-2  ", "  SN-123  ", "  Notes  ", null, null, null, null, null);

        var expectedResponse = new AssetResponseModel(
            1, "CNC Mill", AssetType.Machine, "Shop Floor", "Haas", "VF-2",
            "SN-123", AssetStatus.Active, null, 0, null,
            false, null, null, 0, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);

        _assetRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var command = new CreateAssetCommand(data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _assetRepo.Verify(r => r.AddAsync(It.Is<Asset>(a =>
            a.Name == "CNC Mill" &&
            a.Location == "Shop Floor" &&
            a.Manufacturer == "Haas" &&
            a.Model == "VF-2" &&
            a.SerialNumber == "SN-123" &&
            a.Notes == "Notes"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ToolingType_SetsToolingFields()
    {
        // Arrange
        var data = new CreateAssetRequestModel(
            "Injection Mold", AssetType.Tooling, null, null, null, null, null,
            true, 4, 500000, 10, 20);

        var expectedResponse = new AssetResponseModel(
            1, "Injection Mold", AssetType.Tooling, null, null, null, null,
            AssetStatus.Active, null, 0, null,
            true, 4, 500000, 0, 10, "JOB-00010", 20, "PART-00020", DateTime.UtcNow, DateTime.UtcNow);

        _assetRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var command = new CreateAssetCommand(data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _assetRepo.Verify(r => r.AddAsync(It.Is<Asset>(a =>
            a.AssetType == AssetType.Tooling &&
            a.IsCustomerOwned == true &&
            a.CavityCount == 4 &&
            a.ToolLifeExpectancy == 500000 &&
            a.SourceJobId == 10 &&
            a.SourcePartId == 20
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NullOptionalFields_DefaultsCorrectly()
    {
        // Arrange
        var data = new CreateAssetRequestModel(
            "Basic Asset", AssetType.Other, null, null, null, null, null,
            null, null, null, null, null);

        var expectedResponse = new AssetResponseModel(
            1, "Basic Asset", AssetType.Other, null, null, null, null,
            AssetStatus.Active, null, 0, null,
            false, null, null, 0, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);

        _assetRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var command = new CreateAssetCommand(data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _assetRepo.Verify(r => r.AddAsync(It.Is<Asset>(a =>
            a.IsCustomerOwned == false &&
            a.CavityCount == null &&
            a.ToolLifeExpectancy == null &&
            a.SourceJobId == null &&
            a.SourcePartId == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
