namespace Forge.Core.Models;

/// <summary>A sales-channel order that a channel return can be recorded against, with its returnable lines.</summary>
public record ChannelReturnOrderModel(
    int SalesOrderId,
    string OrderNumber,
    string? ExternalOrderNumber,
    int ChannelId,
    string ChannelName,
    string? BuyerName,
    string CustomerName,
    DateTimeOffset CreatedAt,
    List<ChannelReturnOrderLineModel> Lines);
