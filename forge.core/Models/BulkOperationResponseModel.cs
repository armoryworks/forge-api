namespace Forge.Core.Models;

public record BulkOperationResponseModel(
    int SuccessCount,
    int FailureCount,
    List<BulkOperationError> Errors);
