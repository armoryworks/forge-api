using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IShipmentRepository
{
    Task<List<ShipmentListItemModel>> GetAllAsync(int? salesOrderId, ShipmentStatus? status, CancellationToken ct);
    Task<Shipment?> FindAsync(int id, CancellationToken ct);
    Task<Shipment?> FindWithDetailsAsync(int id, CancellationToken ct);
    Task<string> GenerateNextShipmentNumberAsync(CancellationToken ct);

    /// <summary>True when <paramref name="shipmentNumber"/> is already used by another shipment (excluding <paramref name="excludeId"/>).</summary>
    Task<bool> ShipmentNumberExistsAsync(string shipmentNumber, int? excludeId, CancellationToken ct);
    Task AddAsync(Shipment shipment, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
