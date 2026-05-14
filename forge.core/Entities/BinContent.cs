
namespace Forge.Core.Entities;

public class BinContent : BaseEntity
{
    public int LocationId { get; set; }
    public string EntityType { get; set; } = "part"; // part, production_run, assembly, tooling
    public int EntityId { get; set; }
    public decimal Quantity { get; set; }
    public string? LotNumber { get; set; }
    public int? JobId { get; set; }
    public BinContentStatus Status { get; set; } = BinContentStatus.Stored;
    public int PlacedBy { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public int? RemovedBy { get; set; }
    public string? Notes { get; set; }
    public int? UomId { get; set; }

    public decimal ReservedQuantity { get; set; }

    public StorageLocation Location { get; set; } = null!;
    public Job? Job { get; set; }
    public UnitOfMeasure? Uom { get; set; }
    public ICollection<Reservation> Reservations { get; set; } = [];
}
