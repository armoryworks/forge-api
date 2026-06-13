namespace Forge.Core.Models;

/// <summary>⚡ BANKING BOUNDARY — assemble a zero-dollar prenote batch for all Approved bank accounts.</summary>
public record CreatePrenoteBatchRequestModel(
    DateTimeOffset EffectiveEntryDate);
