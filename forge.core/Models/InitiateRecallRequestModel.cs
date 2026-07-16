namespace Forge.Core.Models;

/// <summary>
/// Initiates a lot-based recall (CAP-QC-RECALL). The recalled lot is the root of a forward
/// genealogy trace to every downstream produced lot; the handler resolves affected
/// customers/shipments and quarantines matching on-hand.
/// </summary>
public record InitiateRecallRequestModel(
    int RecalledLotId,
    string Reason,
    DateTimeOffset RecallDate);
