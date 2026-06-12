using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using indice.Edi;

using Forge.Api.Features.Edi.X12;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;
using Serilog;

namespace Forge.Api.Features.Edi;

/// <summary>
/// ⚡ EDI BOUNDARY — the REAL <see cref="IEdiService"/> (EDI_CORE_PLAN §4): EDI.Net (MIT)
/// deserializes inbound X12; <see cref="X12DocumentWriter"/> renders the outbound set.
/// <list type="bullet">
///   <item><b>Inbound 850 → draft SalesOrder.</b> Parse (EDI.Net, partner-validated), then apply:
///         the trading partner's linked Customer gets a Draft SO — CustomerPO from BEG03, lines
///         from PO1 (part resolved by PO106/PO108 against Part.PartNumber; unresolved lines keep
///         the partner's number in the description for human completion). A 997 acknowledgment is
///         generated + persisted automatically on successful processing.</item>
///   <item><b>Failures are data, not crashes:</b> a parse/apply error lands the transaction in
///         Error with the message recorded — the existing retry endpoint re-runs it.</item>
///   <item><b>Contract parity with the scaffold:</b> Receive/Generate* return UNSAVED transactions
///         (callers persist — see ReceiveEdiDocument / SendOutboundEdi); Parse/Process operate on
///         persisted rows and save their own state transitions.</item>
/// </list>
/// Envelope identity convention (documented here because the scaffold left it open):
/// <c>InterchangeSenderId</c>/<c>ApplicationSenderId</c> = the PARTNER's IDs as they appear on
/// THEIR inbound files; <c>InterchangeReceiverId</c>/<c>ApplicationReceiverId</c> = OUR IDs.
/// Outbound files therefore flip them (we send, they receive).
/// </summary>
public sealed class X12EdiService(
    AppDbContext db,
    ISalesOrderRepository salesOrderRepo,
    IEdiTransportService transport,
    IClock clock) : IEdiService
{
    // ── Inbound ───────────────────────────────────────────────────────────

    public Task<EdiTransaction> ReceiveDocumentAsync(string rawPayload, int tradingPartnerId, CancellationToken ct)
    {
        // Sniff the transaction set + interchange control from the raw envelope (cheap, no full
        // parse — receipt must succeed even for documents we can't process yet).
        var transactionSet = SniffTransactionSet(rawPayload);
        var controlNumber = SniffIsaControlNumber(rawPayload);

        return Task.FromResult(new EdiTransaction
        {
            TradingPartnerId = tradingPartnerId,
            Direction = EdiDirection.Inbound,
            TransactionSet = transactionSet ?? "UNKNOWN",
            ControlNumber = controlNumber,
            RawPayload = rawPayload,
            Status = EdiTransactionStatus.Received,
            ReceivedAt = clock.UtcNow,
            PayloadSizeBytes = rawPayload.Length,
        });
    }

    public async Task<EdiTransaction> ParseTransactionAsync(int transactionId, CancellationToken ct)
    {
        var transaction = await LoadAsync(transactionId, ct);
        try
        {
            var interchange = Deserialize850(transaction.RawPayload);
            ValidatePartnerIdentity(interchange, transaction.TradingPartner);

            var order = FirstOrder(interchange);
            transaction.ParsedDataJson = JsonSerializer.Serialize(new
            {
                interchange.SenderId,
                interchange.ReceiverId,
                interchange.ControlNumber,
                poNumber = order.PurchaseOrderNumber,
                poDate = order.PurchaseOrderDate,
                lineCount = order.Lines?.Count ?? 0,
                lines = (order.Lines ?? []).Select(l => new
                {
                    l.LineNumber, l.Quantity, l.UnitOfMeasure, l.UnitPrice,
                    partNumber = PreferredPartNumber(l), l.Description,
                }),
            });
            transaction.Status = EdiTransactionStatus.Parsed;
            transaction.ErrorMessage = null;
        }
        catch (Exception ex)
        {
            RecordError(transaction, ex);
        }

        await db.SaveChangesAsync(ct);
        return transaction;
    }

    public async Task<EdiTransaction> ProcessTransactionAsync(int transactionId, CancellationToken ct)
    {
        var transaction = await LoadAsync(transactionId, ct);

        if (transaction.Direction != EdiDirection.Inbound || transaction.TransactionSet != "850")
        {
            transaction.Status = EdiTransactionStatus.Error;
            transaction.ErrorMessage = $"Processing is only implemented for inbound 850 (got {transaction.TransactionSet}).";
            await db.SaveChangesAsync(ct);
            return transaction;
        }

        // Idempotency: an already-applied transaction never creates a second sales order.
        if (transaction.Status is EdiTransactionStatus.Applied or EdiTransactionStatus.Acknowledged)
            return transaction;

        try
        {
            var interchange = Deserialize850(transaction.RawPayload);
            ValidatePartnerIdentity(interchange, transaction.TradingPartner);
            var order = FirstOrder(interchange);

            var customerId = transaction.TradingPartner.CustomerId
                ?? throw new InvalidOperationException(
                    $"Trading partner '{transaction.TradingPartner.Name}' has no linked customer — an 850 cannot become a sales order.");

            if (order.Lines is not { Count: > 0 })
                throw new InvalidOperationException("The 850 contains no PO1 line items.");

            // Duplicate-PO guard: the same customer PO number applied twice is the classic EDI
            // double-order incident. (Partner retransmissions arrive with the same BEG03.)
            var duplicate = await db.SalesOrders
                .AnyAsync(s => s.CustomerId == customerId && s.CustomerPO == order.PurchaseOrderNumber, ct);
            if (duplicate)
                throw new InvalidOperationException(
                    $"A sales order for customer PO '{order.PurchaseOrderNumber}' already exists (duplicate 850).");

            var salesOrder = new SalesOrder
            {
                OrderNumber = await salesOrderRepo.GenerateNextOrderNumberAsync(ct),
                CustomerId = customerId,
                Status = SalesOrderStatus.Draft,
                CustomerPO = order.PurchaseOrderNumber,
                Notes = $"Created from EDI 850 (transaction {transaction.Id}, partner {transaction.TradingPartner.Name}).",
            };

            var lineNumber = 1;
            foreach (var line in order.Lines)
            {
                var partnerPartNumber = PreferredPartNumber(line);
                var part = partnerPartNumber is null
                    ? null
                    : await db.Parts.FirstOrDefaultAsync(p => p.PartNumber == partnerPartNumber, ct);

                salesOrder.Lines.Add(new SalesOrderLine
                {
                    PartId = part?.Id,
                    Description = part?.Description
                        ?? line.Description
                        ?? $"EDI item {partnerPartNumber ?? line.LineNumber ?? lineNumber.ToString()}",
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    LineNumber = lineNumber++,
                    // Unresolved part: keep the partner's number visible for human completion.
                    Notes = part is null && partnerPartNumber is not null
                        ? $"Unresolved partner part number: {partnerPartNumber}"
                        : null,
                });
            }

            db.SalesOrders.Add(salesOrder);
            await db.SaveChangesAsync(ct); // assign the SO id for linkage + activity

            transaction.Status = EdiTransactionStatus.Applied;
            transaction.RelatedEntityType = "SalesOrder";
            transaction.RelatedEntityId = salesOrder.Id;
            transaction.ProcessedAt = clock.UtcNow;
            transaction.ErrorMessage = null;

            db.LogActivityAt(
                "created",
                $"Sales order {salesOrder.OrderNumber} created from EDI 850 PO {order.PurchaseOrderNumber} "
                + $"({salesOrder.Lines.Count} line(s))",
                ("SalesOrder", salesOrder.Id));

            // 997 functional acknowledgment — generated + persisted as part of successful processing.
            var ack = await Generate997Async(transaction.Id, ct);
            db.EdiTransactions.Add(ack);
            await db.SaveChangesAsync(ct);

            transaction.AcknowledgmentTransactionId = ack.Id;
            transaction.IsAcknowledged = true;
            transaction.Status = EdiTransactionStatus.Acknowledged;
            await db.SaveChangesAsync(ct);

            Log.Information(
                "EDI 850 {TransactionId} applied: SO {OrderNumber} for partner {Partner}; 997 {AckId} generated.",
                transaction.Id, salesOrder.OrderNumber, transaction.TradingPartner.Name, ack.Id);
        }
        catch (Exception ex)
        {
            RecordError(transaction, ex);
            await db.SaveChangesAsync(ct);
        }

        return transaction;
    }

    public Task RetryTransactionAsync(int transactionId, CancellationToken ct)
        // The retry endpoint already reset the row to Received; re-run the apply pipeline.
        => ProcessTransactionAsync(transactionId, ct);

    // ── Outbound ──────────────────────────────────────────────────────────

    public async Task<EdiTransaction> GeneratePoAckAsync(int salesOrderId, int tradingPartnerId, CancellationToken ct)
    {
        var partner = await PartnerAsync(tradingPartnerId, ct);
        var salesOrder = await db.SalesOrders
            .Include(s => s.Lines).ThenInclude(l => l.Part)
            .FirstOrDefaultAsync(s => s.Id == salesOrderId, ct)
            ?? throw new KeyNotFoundException($"Sales order {salesOrderId} not found");

        var env = await EnvelopeAsync(partner, ct);
        var payload = X12DocumentWriter.Write855(
            env,
            salesOrder.CustomerPO ?? salesOrder.OrderNumber,
            DateOnly.FromDateTime((salesOrder.ConfirmedDate ?? salesOrder.CreatedAt).UtcDateTime),
            salesOrder.Lines.OrderBy(l => l.LineNumber).Select(l =>
                (l.LineNumber.ToString(), l.Quantity, "EA", l.UnitPrice, l.Part?.PartNumber ?? l.Description)).ToList());

        return Outbound(partner, "855", env.ControlNumber, payload, "SalesOrder", salesOrderId);
    }

    public async Task<EdiTransaction> GenerateAsnAsync(int shipmentId, int tradingPartnerId, CancellationToken ct)
    {
        var partner = await PartnerAsync(tradingPartnerId, ct);
        var shipment = await db.Shipments
            .Include(s => s.Lines).ThenInclude(l => l.Part)
            .Include(s => s.SalesOrder)
            .FirstOrDefaultAsync(s => s.Id == shipmentId, ct)
            ?? throw new KeyNotFoundException($"Shipment {shipmentId} not found");

        var env = await EnvelopeAsync(partner, ct);
        var lineNumber = 0;
        var payload = X12DocumentWriter.Write856(
            env,
            shipment.ShipmentNumber,
            shipment.ShippedDate ?? clock.UtcNow,
            shipment.Carrier,
            shipment.TrackingNumber,
            shipment.SalesOrder?.CustomerPO ?? shipment.SalesOrder?.OrderNumber ?? string.Empty,
            shipment.Lines.Select(l =>
                ((++lineNumber).ToString(), l.Quantity, "EA", l.Part?.PartNumber ?? l.Description ?? string.Empty)).ToList());

        return Outbound(partner, "856", env.ControlNumber, payload, "Shipment", shipmentId);
    }

    public async Task<EdiTransaction> GenerateInvoiceEdiAsync(int invoiceId, int tradingPartnerId, CancellationToken ct)
    {
        var partner = await PartnerAsync(tradingPartnerId, ct);
        var invoice = await db.Invoices
            .Include(i => i.Lines).ThenInclude(l => l.Part)
            .Include(i => i.SalesOrder)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new KeyNotFoundException($"Invoice {invoiceId} not found");

        var env = await EnvelopeAsync(partner, ct);
        var payload = X12DocumentWriter.Write810(
            env,
            invoice.InvoiceNumber,
            DateOnly.FromDateTime(invoice.InvoiceDate.UtcDateTime),
            invoice.SalesOrder?.CustomerPO,
            invoice.Lines.Sum(l => l.LineTotal),
            invoice.Lines.OrderBy(l => l.LineNumber).Select(l =>
                (l.LineNumber.ToString(), l.Quantity, "EA", l.UnitPrice, l.Part?.PartNumber ?? l.Description)).ToList());

        return Outbound(partner, "810", env.ControlNumber, payload, "Invoice", invoiceId);
    }

    public async Task<EdiTransaction> Generate997Async(int inboundTransactionId, CancellationToken ct)
    {
        var inbound = await LoadAsync(inboundTransactionId, ct);
        var partner = inbound.TradingPartner;

        // Acknowledge the inbound functional group; re-read its GS control from the payload.
        var interchange = Deserialize850(inbound.RawPayload);
        var group = interchange.Groups?.FirstOrDefault();

        var env = await EnvelopeAsync(partner, ct);
        var payload = X12DocumentWriter.Write997(
            env,
            ackedFunctionalCode: group?.FunctionalIdentifierCode ?? "PO",
            ackedGroupControlNumber: group?.GroupControlNumber ?? 0,
            transactionSetCount: group?.Orders?.Count ?? 1,
            accepted: true);

        var ack = Outbound(partner, "997", env.ControlNumber, payload, null, null);
        ack.AcknowledgmentTransactionId = inboundTransactionId;
        return ack;
    }

    // ── Transport (the Phase-B seam — mock channel today) ─────────────────

    public async Task SendTransactionAsync(int transactionId, CancellationToken ct)
    {
        var transaction = await LoadAsync(transactionId, ct);
        await transport.SendAsync(
            transaction.RawPayload, transaction.TradingPartner.TransportConfigJson ?? "{}", ct);
    }

    public async Task<IReadOnlyList<EdiTransaction>> PollInboundAsync(int tradingPartnerId, CancellationToken ct)
    {
        var partner = await PartnerAsync(tradingPartnerId, ct);
        var payloads = await transport.PollAsync(partner.TransportConfigJson ?? "{}", ct);

        var transactions = new List<EdiTransaction>(payloads.Count);
        foreach (var payload in payloads)
            transactions.Add(await ReceiveDocumentAsync(payload, tradingPartnerId, ct));
        return transactions;
    }

    // ── Internals ─────────────────────────────────────────────────────────

    private static Edi850Interchange Deserialize850(string x12)
    {
        using var reader = new StringReader(x12);
        var interchange = new EdiSerializer().Deserialize<Edi850Interchange>(reader, EdiGrammar.NewX12());
        if (interchange is null)
            throw new InvalidOperationException("The payload did not parse as an X12 interchange.");
        return interchange;
    }

    private static Edi850Interchange.Order FirstOrder(Edi850Interchange interchange)
        => interchange.Groups?.FirstOrDefault()?.Orders?.FirstOrDefault()
            ?? throw new InvalidOperationException("The interchange contains no 850 transaction set.");

    /// <summary>
    /// The ISA sender must be the configured partner — a payload routed to the wrong partner id
    /// must fail loudly, not create another customer's sales order.
    /// </summary>
    private static void ValidatePartnerIdentity(Edi850Interchange interchange, EdiTradingPartner partner)
    {
        var expected = (partner.InterchangeSenderId ?? partner.QualifierValue)?.Trim();
        var actual = interchange.SenderId?.Trim();
        if (!string.IsNullOrEmpty(expected)
            && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Interchange sender '{actual}' does not match trading partner '{partner.Name}' (expected '{expected}').");
        }
    }

    /// <summary>PO106/PO108 by qualifier preference: BP (buyer's) first, then VP (vendor's), then either.</summary>
    private static string? PreferredPartNumber(Edi850Interchange.Line line)
    {
        var pairs = new[] { (line.PartQualifier1, line.PartNumber1), (line.PartQualifier2, line.PartNumber2) };
        return pairs.FirstOrDefault(p => string.Equals(p.Item1, "BP", StringComparison.OrdinalIgnoreCase)).Item2
            ?? pairs.FirstOrDefault(p => string.Equals(p.Item1, "VP", StringComparison.OrdinalIgnoreCase)).Item2
            ?? line.PartNumber1
            ?? line.PartNumber2;
    }

    private async Task<EdiTradingPartner> PartnerAsync(int tradingPartnerId, CancellationToken ct)
        => await db.EdiTradingPartners.FirstOrDefaultAsync(p => p.Id == tradingPartnerId, ct)
            ?? throw new KeyNotFoundException($"Trading partner {tradingPartnerId} not found");

    private async Task<EdiTransaction> LoadAsync(int transactionId, CancellationToken ct)
        => await db.EdiTransactions
            .Include(t => t.TradingPartner)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new KeyNotFoundException($"EDI transaction {transactionId} not found");

    /// <summary>Next outbound interchange control number for the partner (monotonic per partner).</summary>
    private async Task<X12Envelope> EnvelopeAsync(EdiTradingPartner partner, CancellationToken ct)
    {
        var outboundCount = await db.EdiTransactions
            .IgnoreQueryFilters()
            .CountAsync(t => t.TradingPartnerId == partner.Id && t.Direction == EdiDirection.Outbound, ct);

        return new X12Envelope(
            OurQualifier: partner.QualifierId is { Length: > 0 } q ? q : "ZZ",
            OurId: partner.InterchangeReceiverId ?? "FORGE",
            TheirQualifier: partner.QualifierId is { Length: > 0 } tq ? tq : "ZZ",
            TheirId: partner.InterchangeSenderId ?? partner.QualifierValue,
            OurGsId: partner.ApplicationReceiverId ?? partner.InterchangeReceiverId ?? "FORGE",
            TheirGsId: partner.ApplicationSenderId ?? partner.InterchangeSenderId ?? partner.QualifierValue,
            ControlNumber: outboundCount + 1,
            CreatedAt: clock.UtcNow,
            Production: partner.TestModePartnerId is null);
    }

    private EdiTransaction Outbound(
        EdiTradingPartner partner, string transactionSet, int controlNumber, string payload,
        string? relatedEntityType, int? relatedEntityId)
        => new()
        {
            TradingPartnerId = partner.Id,
            Direction = EdiDirection.Outbound,
            TransactionSet = transactionSet,
            ControlNumber = controlNumber.ToString("D9"),
            RawPayload = payload,
            PayloadSizeBytes = payload.Length,
            Status = EdiTransactionStatus.Applied,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            ProcessedAt = clock.UtcNow,
        };

    /// <summary>"ST*850*0001~" → "850" (delimiter-aware: element separator is ISA position 3).</summary>
    private static string? SniffTransactionSet(string x12)
    {
        if (x12.Length < 106 || !x12.StartsWith("ISA", StringComparison.Ordinal))
            return null;
        var elementSeparator = x12[3];
        var marker = $"ST{elementSeparator}";
        var index = x12.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return null;
        var start = index + marker.Length;
        var end = x12.IndexOf(elementSeparator, start);
        return end > start ? x12[start..end] : null;
    }

    private static string? SniffIsaControlNumber(string x12)
    {
        if (!x12.StartsWith("ISA", StringComparison.Ordinal))
            return null;
        var elements = x12.Split(x12[3]);
        return elements.Length > 13 ? elements[13].Trim() : null;
    }

    private void RecordError(EdiTransaction transaction, Exception ex)
    {
        transaction.Status = EdiTransactionStatus.Error;
        transaction.ErrorMessage = ex.Message;
        transaction.ErrorDetailJson = JsonSerializer.Serialize(new { type = ex.GetType().Name, ex.StackTrace });
        Log.Warning(ex, "EDI transaction {TransactionId} failed: {Message}", transaction.Id, ex.Message);
    }
}
