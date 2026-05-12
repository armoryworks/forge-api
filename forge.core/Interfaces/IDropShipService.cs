using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IDropShipService
{
    Task<PurchaseOrder> CreateDropShipPurchaseOrderAsync(int salesOrderLineId, int vendorId, CancellationToken ct);
    Task ConfirmDropShipDeliveryAsync(int purchaseOrderLineId, decimal deliveredQuantity, string? trackingNumber, CancellationToken ct);
    Task<IReadOnlyList<DropShipStatusResponseModel>> GetPendingDropShipsAsync(CancellationToken ct);
}
