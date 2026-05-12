using FluentAssertions;
using Moq;

using Forge.Api.Features.TrackTypes;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.TrackTypes;

public class CreateTrackTypeHandlerTests
{
    private readonly Mock<ITrackTypeRepository> _repo = new();

    [Fact]
    public async Task Handle_ValidCommand_CreatesTrackTypeWithStages()
    {
        // Arrange
        _repo.Setup(r => r.CodeExistsAsync("RND", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repo.Setup(r => r.GetMaxSortOrderAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var expectedResponse = new TrackTypeResponseModel(
            1, "R&D", "RND", "Research and Development", false, 3, []);
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var command = new CreateTrackTypeCommand("R&D", "RND", "Research and Development",
        [
            new StageRequestModel("Design", "DES", 1, "#3b82f6", null, false),
            new StageRequestModel("Prototype", "PROTO", 2, "#22c55e", 5, false),
        ]);

        var handler = new CreateTrackTypeHandler(_repo.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("R&D");
        result.Code.Should().Be("RND");

        _repo.Verify(r => r.AddAsync(It.Is<TrackType>(tt =>
            tt.Name == "R&D" &&
            tt.Code == "RND" &&
            tt.SortOrder == 3 &&
            tt.IsDefault == false &&
            tt.Stages.Count == 2
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateCode_ThrowsInvalidOperationException()
    {
        // Arrange
        _repo.Setup(r => r.CodeExistsAsync("PROD", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var command = new CreateTrackTypeCommand("Production", "PROD", null,
            [new StageRequestModel("Stage 1", "S1", 1, "#000", null, false)]);

        var handler = new CreateTrackTypeHandler(_repo.Object);

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*PROD*already exists*");
    }

    [Fact]
    public async Task Handle_StagesCreatedWithCorrectProperties()
    {
        // Arrange
        _repo.Setup(r => r.CodeExistsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repo.Setup(r => r.GetMaxSortOrderAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        TrackType? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<TrackType>(), It.IsAny<CancellationToken>()))
            .Callback<TrackType, CancellationToken>((tt, _) => captured = tt);
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrackTypeResponseModel(1, "Test", "TST", null, false, 1, []));

        var command = new CreateTrackTypeCommand("Test", "TST", null,
        [
            new StageRequestModel("Open", "OPEN", 1, "#22c55e", 10, false),
        ]);

        var handler = new CreateTrackTypeHandler(_repo.Object);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        var stage = captured!.Stages.Single();
        stage.Name.Should().Be("Open");
        stage.Code.Should().Be("OPEN");
        stage.Color.Should().Be("#22c55e");
        stage.WIPLimit.Should().Be(10);
        stage.IsIrreversible.Should().BeFalse();
    }
}
