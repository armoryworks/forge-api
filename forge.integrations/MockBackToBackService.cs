using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockBackToBackService(ILogger<MockBackToBackService> logger) : IBackToBackService
{
    public Task<PurchaseOrder> CreateBackToBackOrderAsync(int salesOrderLineId, int vendorId, CancellationToken ct)
    {
        logger.LogInformation("[MockBackToBack] CreateBackToBackOrder for SOLine {SalesOrderLineId}, Vendor {VendorId}", salesOrderLineId, vendorId);
        var po = new PurchaseOrder
        {
            Id = 1,
            PONumber = "PO-B2B-0001",
            VendorId = vendorId,
        };
        return Task.FromResult(po);
    }

    public Task LinkReceiptToSalesOrderAsync(int purchaseOrderLineId, int receivingRecordId, CancellationToken ct)
    {
        logger.LogInformation("[MockBackToBack] LinkReceipt POLine {POLineId}, ReceivingRecord {ReceivingRecordId}",
            purchaseOrderLineId, receivingRecordId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BackToBackStatusResponseModel>> GetPendingBackToBacksAsync(CancellationToken ct)
    {
        logger.LogInformation("[MockBackToBack] GetPendingBackToBacks");
        return Task.FromResult<IReadOnlyList<BackToBackStatusResponseModel>>([]);
    }
}
