namespace Forge.Core.Models;

public record ReceiveEdiDocumentRequestModel
{
    public string RawPayload { get; init; } = string.Empty;
    public int TradingPartnerId { get; init; }
}
