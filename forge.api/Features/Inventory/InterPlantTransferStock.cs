using Forge.Core.Entities;

namespace Forge.Api.Features.Inventory;

/// <summary>
/// The note that ties an inter-plant transfer's bin movements to the transfer, so receiving can find which lots
/// left the source bin.
/// </summary>
public static class InterPlantTransferStock
{
    public static string Reference(InterPlantTransfer transfer) => $"Inter-plant transfer {transfer.TransferNumber}";
}
