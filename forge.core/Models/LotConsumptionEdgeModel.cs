namespace Forge.Core.Models;

/// <summary>
/// One lot-genealogy edge for the traceability view: the counterpart lot plus the
/// linked quantity and production context. Direction is implied by which list it
/// appears in on <see cref="LotTraceabilityResponseModel"/> — <c>ConsumedLots</c>
/// are the inputs that went into this lot (backward), <c>ProducedLots</c> are the
/// outputs this lot was consumed into (forward). Powers CAP-QC-RECALL trace.
/// </summary>
public record LotConsumptionEdgeModel(
    int Id,
    int LotId,
    string LotNumber,
    int PartId,
    string PartNumber,
    decimal Quantity,
    int? JobId,
    int? ProductionRunId,
    DateTimeOffset CreatedAt);
