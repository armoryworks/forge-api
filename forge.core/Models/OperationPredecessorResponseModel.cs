using Forge.Core.Enums;

namespace Forge.Core.Models;

public record OperationPredecessorResponseModel(int OperationId, int StepNumber, PredecessorGate Gate);
