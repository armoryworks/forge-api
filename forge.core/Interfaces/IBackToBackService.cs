using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IBackToBackService
{
    Task<PurchaseOrder> CreateBackToBackOrderAsync(int salesOrderLineId, int vendorId, CancellationToken ct);
    Task LinkReceiptToSalesOrderAsync(int purchaseOrderLineId, int receivingRecordId, CancellationToken ct);
    Task<IReadOnlyList<BackToBackStatusResponseModel>> GetPendingBackToBacksAsync(CancellationToken ct);
}
