using Forge.Core.Enums;

namespace Forge.Core.Models;

public record OperationPredecessorRequestModel(int OperationId, PredecessorGate Gate = PredecessorGate.Quantity);
