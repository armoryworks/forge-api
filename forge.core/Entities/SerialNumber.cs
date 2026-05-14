
namespace Forge.Core.Entities;

public class SerialNumber : BaseAuditableEntity
{
    public int PartId { get; set; }
    public string SerialValue { get; set; } = string.Empty;
    public SerialNumberStatus Status { get; set; } = SerialNumberStatus.Available;
    public int? JobId { get; set; }
    public int? LotRecordId { get; set; }
    public int? CurrentLocationId { get; set; }
    public int? ShipmentLineId { get; set; }
    public int? CustomerId { get; set; }
    public int? ParentSerialId { get; set; }
    public DateTimeOffset? ManufacturedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? ScrappedAt { get; set; }
    public string? Notes { get; set; }

    public Part Part { get; set; } = null!;
    public Job? Job { get; set; }
    public StorageLocation? CurrentLocation { get; set; }
    public SerialNumber? ParentSerial { get; set; }
    public ICollection<SerialNumber> ChildSerials { get; set; } = [];
    public ICollection<SerialHistory> History { get; set; } = [];
}
