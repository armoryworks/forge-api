using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Forge.Api.Features.Notifications;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Notifications;

public class CreateNotificationHandlerTests
{
    private readonly Mock<INotificationRepository> _repo = new();
    private readonly Mock<IHubContext<NotificationHub>> _notificationHub = new();
    private readonly CreateNotificationHandler _handler;
    private readonly Faker _faker = new();

    public CreateNotificationHandlerTests()
    {
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _notificationHub.Setup(h => h.Clients).Returns(mockClients.Object);

        _handler = new CreateNotificationHandler(_repo.Object, _notificationHub.Object);
    }

    [Fact]
    public async Task Handle_ValidData_CreatesNotificationAndBroadcasts()
    {
        // Arrange
        var userId = _faker.Random.Int(1, 50);
        var data = new CreateNotificationRequestModel(
            userId, "assignment", "info", "system",
            "New Assignment", "You have been assigned a job",
            "Job", 42, null);

        var expectedResponse = new NotificationResponseModel(
            1, "assignment", "info", "system",
            "New Assignment", "You have been assigned a job",
            false, false, false, "Job", 42, null, null,
            DateTimeOffset.UtcNow);

        _repo.Setup(r => r.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, _) => n.Id = 1)
            .Returns(Task.CompletedTask);

        _repo.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NotificationResponseModel> { expectedResponse });

        var command = new CreateNotificationCommand(data);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Title.Should().Be("New Assignment");

        _repo.Verify(r => r.AddAsync(It.Is<Notification>(n =>
            n.UserId == userId &&
            n.Type == "assignment" &&
            n.EntityType == "Job" &&
            n.EntityId == 42
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
