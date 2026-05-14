using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public interface IBarcodeService
{
    /// <summary>
    /// Creates a barcode record for the given entity. The barcode value is auto-generated
    /// from the entity's natural identifier (e.g., JobNumber, PartNumber).
    /// </summary>
    Task<Barcode> CreateBarcodeAsync(BarcodeEntityType entityType, int entityId, string naturalIdentifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a barcode by its scanned value. Returns null if not found.
    /// </summary>
    Task<Barcode?> FindByValueAsync(string value, CancellationToken cancellationToken = default);
}
