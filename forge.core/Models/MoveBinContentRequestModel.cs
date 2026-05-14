
namespace Forge.Core.Models;

public record MoveBinContentRequestModel(
    int FromLocationId,
    int ToLocationId,
    decimal Quantity,
    BinMovementReason Reason);
