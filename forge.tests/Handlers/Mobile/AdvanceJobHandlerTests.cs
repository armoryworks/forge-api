using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Mobile;
using Forge.Api.Services;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Mobile;

public class AdvanceJobHandlerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IScanCollapseService> _collapse = new();
    private readonly AdvanceJobHandler _handler;

    public AdvanceJobHandlerTests()
    {
        _handler = new AdvanceJobHandler(_mediator.Object, _collapse.Object);
    }

    [Fact]
    public async Task Handle_AtFinalStatus_ThrowsPlainMessage()
    {
        var status = new JobStatusResponseModel(
            7, "JOB-0007", "Bracket", null, 3, "Shipped", "#000000", null, false,
            null, null, 2, "QC", 1, new List<ActivityResponseModel>());
        _mediator.Setup(m => m.Send(It.IsAny<GetJobStatusQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        var act = () => _handler.Handle(new AdvanceJobCommand(7, "device-1", null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This work order is already at its final status.");
        _mediator.Verify(m => m.Send(It.IsAny<MoveJobStageCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
