using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Integrations;

public class MockEdiService : IEdiService
{
    private readonly ILogger<MockEdiService> _logger;

    public MockEdiService(ILogger<MockEdiService> logger)
    {
        _logger = logger;
    }

    public Task<EdiTransaction> ReceiveDocumentAsync(string rawPayload, int tradingPartnerId, CancellationToken ct)
    {
        _logger.LogInformation("[MockEDI] Received document from partner {PartnerId}, payload size: {Size}",
            tradingPartnerId, rawPayload.Length);

        var transaction = new EdiTransaction
        {
            TradingPartnerId = tradingPartnerId,
            Direction = EdiDirection.Inbound,
            TransactionSet = "850",
            RawPayload = rawPayload,
            Status = EdiTransactionStatus.Received,
            ReceivedAt = DateTimeOffset.UtcNow,
            PayloadSizeBytes = rawPayload.Length
        };

        return Task.FromResult(transaction);
    }

    public Task<EdiTransaction> ParseTransactionAsync(int transactionId, CancellationToken ct)
    {
        _logger.LogInformation("[MockEDI] Parsing transaction {TransactionId}", transactionId);
        return Task.FromResult(new EdiTransaction
        {
            Id = transactionId,
            Status = EdiTransactionStatus.Parsed,
            ParsedDataJson = "{\"mock\": true}"
        });
    }

    public Task<EdiTransaction> ProcessTransactionAsync(int transactionId, CancellationToken ct)
    {
        _logger.LogInformation("[MockEDI] Processing transaction {TransactionId}", transactionId);
        return Task.FromResult(new EdiTransaction
        {
            Id = transactionId,
            Status = EdiTransactionStatus.Applied
        });
    }

    public Task RetryTransactionAsync(int transactionId, CancellationToken ct)
    {
        _logger.LogInformation("[MockEDI] Retrying transaction {TransactionId}", transactionId);
        return Task.CompletedTask;
    }

    public Task<EdiTransaction> GenerateAsnAsync(int shipmentId, int tradingPartnerId, CancellationToken ct)
    {
        _logger.LogInformation("[MockEDI] Generating ASN (856) for shipment {ShipmentId}", shipmentId);
        return Task.FromResult(new EdiTransaction
        {
            TradingPartnerId = tradingPartnerId,
            Direction = EdiDirection.Outbound,
            TransactionSet = "856",
            RawPayload = "ISA*00*          *00*          *ZZ*MOCK           *ZZ*PARTNER        *260412*1200*U*00401*000000001*0*P*>~",
            Status = EdiTransactionStatus.Applied,
            RelatedEntityType = "Shipment",
            RelatedEntityId = shipmentId
        });
    }

    public Task<EdiTransaction> GenerateInvoiceEdiAsync(int invoiceId, int tradingPartnerId, CancellationToken ct)
    {
        _logger.LogInformation("[MockEDI] Generating Invoice EDI (810) for invoice {InvoiceId}", invoiceId);
        return Task.FromResult(new EdiTransaction
        {
            TradingPartnerId = tradingPartnerId,
            Direction = EdiDirection.Outbound,
            TransactionSet = "810",
            RawPayload = "ISA*00*          *00*          *ZZ*MOCK           *ZZ*PARTNER        *260412*1200*U*00401*000000002*0*P*>~",
            Status = EdiTransactionStatus.Applied,
            RelatedEntityType = "Invoice",
            RelatedEntityId = invoiceId
        });
    }

    public Task<EdiTransaction> GeneratePoAckAsync(int salesOrderId, int tradingPartnerId, CancellationToken ct)
    {
        _logger.LogInformation("[MockEDI] Generating PO Ack (855) for sales order {SalesOrderId}", salesOrderId);
        return Task.FromResult(new EdiTransaction
        {
            TradingPartnerId = tradingPartnerId,
            Direction = EdiDirection.Outbound,
            TransactionSet = "855",
            RawPayload = "ISA*00*          *00*          *ZZ*MOCK           *ZZ*PARTNER        *260412*1200*U*00401*000000003*0*P*>~",
            Status = EdiTransactionStatus.Applied,
            RelatedEntityType = "SalesOrder",
            RelatedEntityId = salesOrderId
        });
    }

    public Task<EdiTransaction> Generate997Async(int inboundTransactionId, CancellationToken ct)
    {
        _logger.LogInformation("[MockEDI] Generating 997 acknowledgment for transaction {TransactionId}", inboundTransactionId);
        return Task.FromResult(new EdiTransaction
        {
            Direction = EdiDirection.Outbound,
            TransactionSet = "997",
            RawPayload = "ISA*00*          *00*          *ZZ*MOCK           *ZZ*PARTNER        *260412*1200*U*00401*000000004*0*P*>~",
            Status = EdiTransactionStatus.Applied,
            AcknowledgmentTransactionId = inboundTransactionId
        });
    }

    public Task SendTransactionAsync(int transactionId, CancellationToken ct)
    {
        _logger.LogInformation("[MockEDI] Sending transaction {TransactionId}", transactionId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EdiTransaction>> PollInboundAsync(int tradingPartnerId, CancellationToken ct)
    {
        _logger.LogInformation("[MockEDI] Polling inbound for partner {PartnerId}", tradingPartnerId);
        return Task.FromResult<IReadOnlyList<EdiTransaction>>(Array.Empty<EdiTransaction>());
    }
}
