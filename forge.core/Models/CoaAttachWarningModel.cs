namespace Forge.Core.Models;

/// <summary>
/// A lot that was not certified for a shipment: <c>coa-nonconforming</c> (a result is out of specification;
/// <see cref="FailedCharacteristics"/> names them) or <c>coa-no-results</c> (the lot has no inspection or SPC results).
/// </summary>
public record CoaAttachWarningModel(
    int LotId,
    string LotNumber,
    string PartNumber,
    string Code,
    List<string> FailedCharacteristics);
