using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

using Forge.Api.Controllers;
using Forge.Api.Features.ShopFloor;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.ShopFloor;

public class ShopFloorClockPunchTests
{
    private readonly Mock<IMediator> _mediator = new();

    private ShopFloorController ControllerFor(ClaimsPrincipal user) => new(_mediator.Object)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
    };

    private static ClaimsPrincipal SignedIn(int userId, string role) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        new Claim(ClaimTypes.Role, role),
    ], "Test"));

    [Fact]
    public async Task Worker_punching_for_themself_is_recorded_as_kiosk()
    {
        var result = await ControllerFor(SignedIn(7, "ProductionWorker"))
            .ClockInOut(new ClockInOutRequestModel(7, "ClockIn"));

        result.Should().BeOfType<NoContentResult>();
        _mediator.Verify(m => m.Send(
            It.Is<ClockInOutCommand>(c => c.UserId == 7 && c.Source == "kiosk"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Worker_punching_for_someone_else_is_forbidden()
    {
        var result = await ControllerFor(SignedIn(7, "ProductionWorker"))
            .ClockInOut(new ClockInOutRequestModel(8, "ClockIn"));

        result.Should().BeOfType<ForbidResult>();
        _mediator.Verify(m => m.Send(It.IsAny<ClockInOutCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    public async Task Supervisor_punching_for_someone_else_is_recorded_as_kiosk_supervisor(string role)
    {
        var result = await ControllerFor(SignedIn(7, role))
            .ClockInOut(new ClockInOutRequestModel(8, "ClockOut"));

        result.Should().BeOfType<NoContentResult>();
        _mediator.Verify(m => m.Send(
            It.Is<ClockInOutCommand>(c => c.UserId == 8 && c.Source == "kiosk-supervisor"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_caller_without_a_user_identity_is_unauthorized()
    {
        var result = await ControllerFor(new ClaimsPrincipal(new ClaimsIdentity()))
            .ClockInOut(new ClockInOutRequestModel(8, "ClockIn"));

        result.Should().BeOfType<UnauthorizedResult>();
    }
}
