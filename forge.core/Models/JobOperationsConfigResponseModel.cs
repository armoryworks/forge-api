namespace Forge.Core.Models;

public record JobOperationsConfigResponseModel(bool OperationTracking, IReadOnlyList<OperationReasonResponseModel> Reasons);
