using System.Security.Claims;

using FluentAssertions;
using Moq;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Activity;
using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Activity;

public class CreateActivityLogHandlerTests
{
    private readonly Mock<IActivityLogRepository> _activityRepo = new();
    private readonly Mock<ISender> _sender = new();
    private readonly Mock<IHttpContextAccessor> _httpContext = new();

    private void SetupAuthenticatedUser(int userId)
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        ], "test"));
        var httpContext = new DefaultHttpContext { User = claims };
        _httpContext.Setup(x => x.HttpContext).Returns(httpContext);
    }

    [Fact]
    public async Task Handle_ValidComment_CreatesActivityLogEntry()
    {
        // Arrange
        SetupAuthenticatedUser(10);

        var expectedActivity = new ActivityResponseModel(
            0, "Comment", null, null, null, "This is a test comment",
            "DH", "Dan Hokanson", DateTimeOffset.UtcNow);

        _activityRepo.Setup(r => r.GetByEntityAsync("job", 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expectedActivity]);

        var handler = new CreateEntityCommentHandler(_activityRepo.Object, _sender.Object, _httpContext.Object);
        var command = new CreateEntityCommentCommand("job", 42, "This is a test comment", []);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Action.Should().Be("Comment");
        result.Description.Should().Be("This is a test comment");

        _activityRepo.Verify(r => r.AddAsync(It.Is<ActivityLog>(a =>
            a.EntityType == "job" &&
            a.EntityId == 42 &&
            a.UserId == 10 &&
            a.Action == "Comment" &&
            a.Description == "This is a test comment"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithMentions_SendsNotifications()
    {
        // Arrange
        SetupAuthenticatedUser(10);

        var expectedActivity = new ActivityResponseModel(
            0, "Comment", null, null, null, "Hey @Dan check this",
            "JD", "Jane Doe", DateTimeOffset.UtcNow);

        _activityRepo.Setup(r => r.GetByEntityAsync("job", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expectedActivity]);

        var handler = new CreateEntityCommentHandler(_activityRepo.Object, _sender.Object, _httpContext.Object);
        var command = new CreateEntityCommentCommand("job", 1, "Hey @Dan check this", [20, 30]);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        _sender.Verify(s => s.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_EmptyMentions_DoesNotSendNotifications()
    {
        // Arrange
        SetupAuthenticatedUser(10);

        var expectedActivity = new ActivityResponseModel(
            0, "Comment", null, null, null, "No mentions here",
            "DH", "Dan Hokanson", DateTimeOffset.UtcNow);

        _activityRepo.Setup(r => r.GetByEntityAsync("part", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expectedActivity]);

        var handler = new CreateEntityCommentHandler(_activityRepo.Object, _sender.Object, _httpContext.Object);
        var command = new CreateEntityCommentCommand("part", 5, "No mentions here", []);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        _sender.Verify(s => s.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
