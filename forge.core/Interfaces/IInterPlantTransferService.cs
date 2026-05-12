using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IInterPlantTransferService
{
    Task<InterPlantTransfer> CreateTransferAsync(CreateInterPlantTransferRequestModel request, CancellationToken ct);
    Task ShipTransferAsync(int transferId, string? trackingNumber, CancellationToken ct);
    Task ReceiveTransferAsync(int transferId, IReadOnlyList<ReceiveTransferLineRequestModel> lines, CancellationToken ct);
}
