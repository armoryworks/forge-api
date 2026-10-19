using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Features.Mobile;
using Forge.Api.Features.ShopFloor;

namespace Forge.Tests.Handlers.Mobile;

public class RecordClockPunchSourceTests
{
    [Fact]
    public async Task A_punch_from_the_phone_app_is_recorded_as_mobile()
    {
        var mediator = new Mock<IMediator>();
        ClockInOutCommand? sent = null;
        mediator.Setup(m => m.Send(It.IsAny<ClockInOutCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ClockInOutResponseModel>, CancellationToken>((c, _) => sent = (ClockInOutCommand)c)
            .ReturnsAsync(new ClockInOutResponseModel(11, null));
        mediator.Setup(m => m.Send(It.IsAny<GetClockStateQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClockStateResponseModel("in", "ClockIn", DateTimeOffset.UtcNow, 11));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7")], "Test")),
        };
        var handler = new RecordClockPunchHandler(mediator.Object, new HttpContextAccessor { HttpContext = context });

        var result = await handler.Handle(new RecordClockPunchCommand("ClockIn"), CancellationToken.None);

        result.EventId.Should().Be(11);
        sent.Should().NotBeNull();
        sent!.UserId.Should().Be(7);
        sent.Source.Should().Be("mobile");
    }
}
