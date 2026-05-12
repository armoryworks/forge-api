using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockDropShipService(ILogger<MockDropShipService> logger) : IDropShipService
{
    public Task<PurchaseOrder> CreateDropShipPurchaseOrderAsync(int salesOrderLineId, int vendorId, CancellationToken ct)
    {
        logger.LogInformation("[MockDropShip] CreateDropShipPO for SOLine {SalesOrderLineId}, Vendor {VendorId}", salesOrderLineId, vendorId);
        var po = new PurchaseOrder
        {
            Id = 1,
            PONumber = "PO-DS-0001",
            VendorId = vendorId,
        };
        return Task.FromResult(po);
    }

    public Task ConfirmDropShipDeliveryAsync(int purchaseOrderLineId, decimal deliveredQuantity, string? trackingNumber, CancellationToken ct)
    {
        logger.LogInformation("[MockDropShip] ConfirmDelivery POLine {POLineId}, Qty={Quantity}, Tracking={Tracking}",
            purchaseOrderLineId, deliveredQuantity, trackingNumber);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DropShipStatusResponseModel>> GetPendingDropShipsAsync(CancellationToken ct)
    {
        logger.LogInformation("[MockDropShip] GetPendingDropShips");
        return Task.FromResult<IReadOnlyList<DropShipStatusResponseModel>>([]);
    }
}
