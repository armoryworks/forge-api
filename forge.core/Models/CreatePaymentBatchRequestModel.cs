namespace Forge.Core.Models;

/// <summary>⚡ BANKING BOUNDARY — assemble a NACHA payment batch from eligible ACH payments.</summary>
public record CreatePaymentBatchRequestModel(
    List<int> VendorPaymentIds,
    DateTimeOffset EffectiveEntryDate);
