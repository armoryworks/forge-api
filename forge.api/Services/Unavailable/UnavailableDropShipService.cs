using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services.Unavailable;

/// <summary>
/// Drop-ship purchasing outside mock posture. No real engine exists yet, so creating or confirming a
/// drop-ship answers 501 instead of returning a made-up purchase order id; the pending list is empty
/// because none can exist.
/// </summary>
public sealed class UnavailableDropShipService : IDropShipService
{
    public const string Message = "Drop-ship orders are not available in this version of Forge.";

    public Task<PurchaseOrder> CreateDropShipPurchaseOrderAsync(int salesOrderLineId, CreateDropShipRequestModel request, CancellationToken ct) =>
        throw new FeatureNotAvailableException(Message);

    public Task ConfirmDropShipDeliveryAsync(int purchaseOrderLineId, decimal deliveredQuantity, string? trackingNumber, CancellationToken ct) =>
        throw new FeatureNotAvailableException(Message);

    public Task<IReadOnlyList<DropShipStatusResponseModel>> GetPendingDropShipsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DropShipStatusResponseModel>>([]);
}
