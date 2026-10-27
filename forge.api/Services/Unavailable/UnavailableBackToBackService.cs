using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services.Unavailable;

/// <summary>
/// Back-to-back purchasing outside mock posture. No real engine exists yet, so creating a back-to-back
/// order or linking its receipt answers 501 instead of returning a made-up purchase order id; the
/// pending list is empty because none can exist.
/// </summary>
public sealed class UnavailableBackToBackService : IBackToBackService
{
    public const string Message = "Back-to-back orders are not available in this version of Forge.";

    public Task<PurchaseOrder> CreateBackToBackOrderAsync(int salesOrderLineId, CreateBackToBackRequestModel request, CancellationToken ct) =>
        throw new FeatureNotAvailableException(Message);

    public Task LinkReceiptToSalesOrderAsync(int purchaseOrderLineId, int receivingRecordId, CancellationToken ct) =>
        throw new FeatureNotAvailableException(Message);

    public Task<IReadOnlyList<BackToBackStatusResponseModel>> GetPendingBackToBacksAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BackToBackStatusResponseModel>>([]);
}
