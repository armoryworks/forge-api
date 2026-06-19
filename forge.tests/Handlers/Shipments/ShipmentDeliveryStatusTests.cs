using FluentAssertions;

using Forge.Api.Features.Shipments;

namespace Forge.Tests.Handlers.Shipments;

public class ShipmentDeliveryStatusTests
{
    [Theory]
    [InlineData("delivered")]
    [InlineData("Delivered")]
    [InlineData("  DELIVERED ")]
    [InlineData("Package delivered to front door")]
    [InlineData("completed")]
    public void Delivered_statuses_are_recognized(string status)
        => ShipmentDeliveryStatus.IsDelivered(status).Should().BeTrue();

    [Theory]
    [InlineData("In Transit")]
    [InlineData("Out for delivery")]
    [InlineData("Exception")]
    [InlineData("Pre-Transit")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_delivered_statuses_are_not(string? status)
        => ShipmentDeliveryStatus.IsDelivered(status).Should().BeFalse();
}
