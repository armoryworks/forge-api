using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class OperationPredecessor
{
    public int OperationId { get; set; }
    public int PredecessorOperationId { get; set; }
    public PredecessorGate Gate { get; set; } = PredecessorGate.Quantity;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Operation Operation { get; set; } = null!;
    public Operation PredecessorOperation { get; set; } = null!;
}
