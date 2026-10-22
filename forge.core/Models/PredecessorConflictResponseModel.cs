namespace Forge.Core.Models;

public record PredecessorConflictResponseModel(
    int OperationId,
    int StepNumber,
    int PredecessorOperationId,
    int PredecessorStepNumber);
