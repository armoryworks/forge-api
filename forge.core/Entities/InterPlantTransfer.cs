
namespace Forge.Core.Entities;

public class InterPlantTransfer : BaseAuditableEntity
{
    public string TransferNumber { get; set; } = string.Empty;
    public int FromPlantId { get; set; }
    public int ToPlantId { get; set; }
    public InterPlantTransferStatus Status { get; set; } = InterPlantTransferStatus.Draft;
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public int? ShippedById { get; set; }
    public int? ReceivedById { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Notes { get; set; }

    public Plant FromPlant { get; set; } = null!;
    public Plant ToPlant { get; set; } = null!;
    public ICollection<InterPlantTransferLine> Lines { get; set; } = [];
}
