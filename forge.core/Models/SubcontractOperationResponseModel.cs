namespace Forge.Core.Models;

public record SubcontractOperationResponseModel(
    int OperationId,
    int StepNumber,
    string Title,
    int VendorId,
    string VendorName,
    decimal? SubcontractCost,
    decimal? TurnTimeDays,
    decimal JobQuantity,
    decimal CommittedQuantity = 0m,
    decimal RemainingQuantity = 0m,
    decimal? MaxSendQuantity = null);
