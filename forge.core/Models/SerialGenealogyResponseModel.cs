using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// One serial in a genealogy tree, with the job it was built on and the lots that went into it: the serial's own
/// lot and the lots issued to its job and its sub-assembly jobs.
/// </summary>
public record SerialGenealogyResponseModel(
    int Id,
    string SerialValue,
    string PartNumber,
    SerialNumberStatus Status,
    List<SerialGenealogyResponseModel> Children,
    int? JobId = null,
    string? JobNumber = null,
    List<SerialGenealogyLotModel>? Lots = null);

/// <summary>A lot in a serial's genealogy. Source is Lot (the serial's own lot) or MaterialIssue (issued to its job or a sub-assembly job).</summary>
public record SerialGenealogyLotModel(
    int? LotId,
    string LotNumber,
    int PartId,
    string PartNumber,
    string Source,
    decimal? Quantity);
