using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public interface IEdiService
{
    // Inbound processing
    Task<EdiTransaction> ReceiveDocumentAsync(string rawPayload, int tradingPartnerId, CancellationToken ct);
    Task<EdiTransaction> ParseTransactionAsync(int transactionId, CancellationToken ct);
    Task<EdiTransaction> ProcessTransactionAsync(int transactionId, CancellationToken ct);
    Task RetryTransactionAsync(int transactionId, CancellationToken ct);

    // Outbound generation
    Task<EdiTransaction> GenerateAsnAsync(int shipmentId, int tradingPartnerId, CancellationToken ct);
    Task<EdiTransaction> GenerateInvoiceEdiAsync(int invoiceId, int tradingPartnerId, CancellationToken ct);
    Task<EdiTransaction> GeneratePoAckAsync(int salesOrderId, int tradingPartnerId, CancellationToken ct);
    Task<EdiTransaction> Generate997Async(int inboundTransactionId, CancellationToken ct);

    // Transport
    Task SendTransactionAsync(int transactionId, CancellationToken ct);
    Task<IReadOnlyList<EdiTransaction>> PollInboundAsync(int tradingPartnerId, CancellationToken ct);
}
