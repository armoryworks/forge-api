namespace Forge.Core.Models;

public record SendPurchaseOrderEmailRequestModel(
    string? To,
    string? Cc,
    string? Message);
