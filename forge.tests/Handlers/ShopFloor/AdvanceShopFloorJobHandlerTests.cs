using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Mobile;
using Forge.Api.Features.ShopFloor;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.ShopFloor;

public class AdvanceShopFloorJobHandlerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly AdvanceShopFloorJobHandler _handler;

    public AdvanceShopFloorJobHandlerTests()
    {
        _handler = new AdvanceShopFloorJobHandler(_mediator.Object);
    }

    private static JobStatusResponseModel Status(
        int stageId, string stageName, int? nextId, string? nextName, bool nextIsShopFloor) =>
        new(42, "JOB-0042", "Bracket", null,
            stageId, stageName, "#000000",
            null, false,
            nextId, nextName, null, null,
            0, new List<ActivityResponseModel>())
        {
            NextStageIsShopFloor = nextIsShopFloor,
        };

    [Fact]
    public async Task Handle_ShopFloorNextStage_MovesAndReturnsThePreviousStage()
    {
        _mediator
            .SetupSequence(m => m.Send(It.IsAny<GetJobStatusQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Status(6, "In Production", 7, "QC/Review", true))
            .ReturnsAsync(Status(7, "QC/Review", 8, "Shipped", true));

        var result = await _handler.Handle(new AdvanceShopFloorJobCommand(42), CancellationToken.None);

        result.Status.StageName.Should().Be("QC/Review");
        result.PreviousStageId.Should().Be(6);
        result.PreviousStageName.Should().Be("In Production");
        result.Collapsed.Should().BeFalse();
        _mediator.Verify(m => m.Send(
            It.Is<MoveJobStageCommand>(c => c.JobId == 42 && c.StageId == 7),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OfficeNextStage_RefusesAndDoesNotMove()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<GetJobStatusQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Status(8, "Shipped", 9, "Invoiced/Sent", false));

        var act = () => _handler.Handle(new AdvanceShopFloorJobCommand(42), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The next status, Invoiced/Sent, is an office status. Move it from the board.");
        _mediator.Verify(m => m.Send(It.IsAny<MoveJobStageCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoNextStage_RefusesAndDoesNotMove()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<GetJobStatusQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Status(11, "Payment Received", null, null, false));

        var act = () => _handler.Handle(new AdvanceShopFloorJobCommand(42), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This work order is already at its final status.");
        _mediator.Verify(m => m.Send(It.IsAny<MoveJobStageCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
