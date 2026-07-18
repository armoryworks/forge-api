using Forge.Core.Entities;
using Forge.Core.Enums;

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

    /// <summary>
    /// Re-syncs a part's active barcode to its current identity: the licensed GTIN (Gs1) when the part
    /// carries one, otherwise the internal self-generated code. Creates the barcode if the part has none.
    /// Called after a GTIN is assigned or removed so the scannable code always matches the part.
    /// </summary>
    Task RefreshPartBarcodeAsync(int partId, CancellationToken cancellationToken = default);
}
