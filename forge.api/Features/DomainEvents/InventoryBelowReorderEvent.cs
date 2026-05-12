using MediatR;

namespace Forge.Api.Features.DomainEvents;

public record InventoryBelowReorderEvent(int PartId, int CurrentQty, int ReorderPoint) : INotification;
