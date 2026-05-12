namespace Forge.Core.Models;

public record BomExplosionBuyItemModel(
    int PartId,
    string PartNumber,
    string Description,
    decimal Quantity,
    int? PreferredVendorId,
    string? PreferredVendorName,
    int? LeadTimeDays);
