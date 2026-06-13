using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Edi;
using Forge.Api.Features.Edi.X12;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Edi;

/// <summary>
/// EDI core (EDI_CORE_PLAN §4) — the REAL X12EdiService over the existing scaffold. Proves:
///   • a golden 850 (EDI.Net inbound parse) becomes a Draft SalesOrder for the partner's
///     customer — CustomerPO from BEG03, lines from PO1 with part resolution by PO107,
///     unresolved parts kept visible in line notes;
///   • a 997 acknowledgment is generated, persisted, and linked on successful processing;
///   • duplicate 850 (same BEG03) → Error, not a second order; wrong-sender → Error;
///     partner without a customer → Error; malformed payload → Error with message;
///   • re-processing an Applied transaction is a no-op (idempotent);
///   • outbound 855/810/997 render structurally valid X12 (fixed-width ISA, SE counts),
///     and the rendered envelope round-trips through EDI.Net's parser.
/// </summary>
public class X12EdiServiceTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 6, 13, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeTransport : IEdiTransportService
    {
        public List<string> Sent { get; } = [];
        public List<string> Inbox { get; } = [];
        public EdiTransportMethod Method => EdiTransportMethod.Sftp;
        public Task SendAsync(string payload, string connectionConfig, CancellationToken ct)
        { Sent.Add(payload); return Task.CompletedTask; }
        public Task<IReadOnlyList<string>> PollAsync(string connectionConfig, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(Inbox.ToList());
        public Task<bool> TestConnectionAsync(string connectionConfig, CancellationToken ct)
            => Task.FromResult(true);
    }

    private sealed class FakeTransportFactory(FakeTransport transport) : IEdiTransportFactory
    {
        public IEdiTransportService For(EdiTransportMethod method) => transport;
    }

    private static (AppDbContext Db, X12EdiService Service, FakeTransport Transport) CreateHarness()
    {
        var db = TestDbContextFactory.Create();
        var transport = new FakeTransport();
        var service = new X12EdiService(
            db, new SalesOrderRepository(db), new FakeTransportFactory(transport), new FakeClock());
        return (db, service, transport);
    }

    private static async Task<(EdiTradingPartner Partner, int CustomerId)> SeedPartnerAsync(
        AppDbContext db, bool linkCustomer = true)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();

        var partner = new EdiTradingPartner
        {
            Name = "Acme EDI",
            CustomerId = linkCustomer ? customer.Id : null,
            QualifierId = "ZZ",
            QualifierValue = "ACMEEDI",
            InterchangeSenderId = "ACMEEDI",
            InterchangeReceiverId = "FORGE",
            ApplicationSenderId = "ACMEEDI",
            ApplicationReceiverId = "FORGE",
            DefaultFormat = EdiFormat.X12,
            AutoProcess = true,
            IsActive = true,
        };
        db.EdiTradingPartners.Add(partner);
        await db.SaveChangesAsync();
        return (partner, customer.Id);
    }

    private static async Task<int> SeedPartAsync(AppDbContext db, string partNumber = "WIDGET-100")
    {
        var part = new Part { PartNumber = partNumber, Description = "Molded widget, blue" };
        db.Set<Part>().Add(part);
        await db.SaveChangesAsync();
        return part.Id;
    }

    /// <summary>Golden inbound 850: 2 lines (one resolvable part, one unknown), ship-to loop.</summary>
    private const string Golden850 =
        "ISA*00*          *00*          *ZZ*ACMEEDI        *ZZ*FORGE          *260613*0900*U*00401*000000042*0*P*>~\n"
        + "GS*PO*ACMEEDI*FORGE*20260613*0900*42*X*004010~\n"
        + "ST*850*0001~\n"
        + "BEG*00*SA*PO-77821**20260613~\n"
        + "N1*ST*Acme Corp Plant 2~\n"
        + "N3*100 Industrial Way~\n"
        + "N4*Dayton*OH*45402~\n"
        + "PO1*1*500*EA*2.5*PE*BP*WIDGET-100~\n"
        + "PID*F****Molded widget, blue~\n"
        + "PO1*2*25*EA*10*PE*BP*UNKNOWN-PART~\n"
        + "PID*F****Specialty bracket~\n"
        + "CTT*2~\n"
        + "SE*11*0001~\n"
        + "GE*1*42~\n"
        + "IEA*1*000000042~\n";

    private static async Task<EdiTransaction> ReceiveAsync(
        AppDbContext db, X12EdiService service, int partnerId, string payload = Golden850)
    {
        var transaction = await service.ReceiveDocumentAsync(payload, partnerId, CancellationToken.None);
        db.EdiTransactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction;
    }

    // ─────────────────────────── Inbound 850 ───────────────────────────

    [Fact]
    public async Task Receive_SniffsTransactionSetAndControlNumber()
    {
        var (db, service, _) = CreateHarness();
        var (partner, _) = await SeedPartnerAsync(db);

        var transaction = await ReceiveAsync(db, service, partner.Id);

        transaction.TransactionSet.Should().Be("850");
        transaction.ControlNumber.Should().Be("000000042");
        transaction.Status.Should().Be(EdiTransactionStatus.Received);
    }

    [Fact]
    public async Task Process_Golden850_CreatesDraftSalesOrder_WithPartResolution()
    {
        var (db, service, _) = CreateHarness();
        var (partner, customerId) = await SeedPartnerAsync(db);
        var partId = await SeedPartAsync(db);
        var transaction = await ReceiveAsync(db, service, partner.Id);

        var result = await service.ProcessTransactionAsync(transaction.Id, CancellationToken.None);

        result.Status.Should().Be(EdiTransactionStatus.Acknowledged);
        result.RelatedEntityType.Should().Be("SalesOrder");

        var salesOrder = await db.SalesOrders.Include(s => s.Lines).SingleAsync();
        salesOrder.CustomerId.Should().Be(customerId);
        salesOrder.Status.Should().Be(SalesOrderStatus.Draft);
        salesOrder.CustomerPO.Should().Be("PO-77821");
        salesOrder.Lines.Should().HaveCount(2);

        var resolved = salesOrder.Lines.Single(l => l.LineNumber == 1);
        resolved.PartId.Should().Be(partId);
        resolved.Quantity.Should().Be(500m);
        resolved.UnitPrice.Should().Be(2.5m);

        // The unknown part stays human-completable: no PartId, partner number kept visible.
        var unresolved = salesOrder.Lines.Single(l => l.LineNumber == 2);
        unresolved.PartId.Should().BeNull();
        unresolved.Description.Should().Be("Specialty bracket");
        unresolved.Notes.Should().Contain("UNKNOWN-PART");
    }

    [Fact]
    public async Task Process_Generates997_AndLinksAcknowledgment()
    {
        var (db, service, _) = CreateHarness();
        var (partner, _) = await SeedPartnerAsync(db);
        await SeedPartAsync(db);
        var transaction = await ReceiveAsync(db, service, partner.Id);

        await service.ProcessTransactionAsync(transaction.Id, CancellationToken.None);

        var ack = await db.EdiTransactions.SingleAsync(t => t.TransactionSet == "997");
        ack.Direction.Should().Be(EdiDirection.Outbound);
        ack.AcknowledgmentTransactionId.Should().Be(transaction.Id);
        // AK1 names the partner's functional group; AK9 accepts it.
        ack.RawPayload.Should().Contain("AK1*PO*42").And.Contain("AK9*A*1*1*1");

        var inbound = await db.EdiTransactions.SingleAsync(t => t.Id == transaction.Id);
        inbound.IsAcknowledged.Should().BeTrue();
        inbound.AcknowledgmentTransactionId.Should().Be(ack.Id);
    }

    [Fact]
    public async Task Process_Duplicate850_SamePoNumber_Errors()
    {
        var (db, service, _) = CreateHarness();
        var (partner, _) = await SeedPartnerAsync(db);
        await SeedPartAsync(db);

        var first = await ReceiveAsync(db, service, partner.Id);
        await service.ProcessTransactionAsync(first.Id, CancellationToken.None);

        var second = await ReceiveAsync(db, service, partner.Id);
        var result = await service.ProcessTransactionAsync(second.Id, CancellationToken.None);

        result.Status.Should().Be(EdiTransactionStatus.Error);
        result.ErrorMessage.Should().Contain("duplicate 850");
        (await db.SalesOrders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Process_Reprocess_IsIdempotent()
    {
        var (db, service, _) = CreateHarness();
        var (partner, _) = await SeedPartnerAsync(db);
        await SeedPartAsync(db);
        var transaction = await ReceiveAsync(db, service, partner.Id);

        await service.ProcessTransactionAsync(transaction.Id, CancellationToken.None);
        await service.ProcessTransactionAsync(transaction.Id, CancellationToken.None);

        (await db.SalesOrders.CountAsync()).Should().Be(1);
        (await db.EdiTransactions.CountAsync(t => t.TransactionSet == "997")).Should().Be(1);
    }

    [Fact]
    public async Task Process_WrongSenderId_Errors()
    {
        var (db, service, _) = CreateHarness();
        var (partner, _) = await SeedPartnerAsync(db);
        var payload = Golden850.Replace("ZZ*ACMEEDI        ", "ZZ*INTRUDER       ");
        var transaction = await ReceiveAsync(db, service, partner.Id, payload);

        var result = await service.ProcessTransactionAsync(transaction.Id, CancellationToken.None);

        result.Status.Should().Be(EdiTransactionStatus.Error);
        result.ErrorMessage.Should().Contain("does not match trading partner");
        (await db.SalesOrders.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Process_PartnerWithoutCustomer_Errors()
    {
        var (db, service, _) = CreateHarness();
        var (partner, _) = await SeedPartnerAsync(db, linkCustomer: false);
        var transaction = await ReceiveAsync(db, service, partner.Id);

        var result = await service.ProcessTransactionAsync(transaction.Id, CancellationToken.None);

        result.Status.Should().Be(EdiTransactionStatus.Error);
        result.ErrorMessage.Should().Contain("no linked customer");
    }

    [Fact]
    public async Task Process_MalformedPayload_ErrorsWithMessage()
    {
        var (db, service, _) = CreateHarness();
        var (partner, _) = await SeedPartnerAsync(db);
        var transaction = await ReceiveAsync(db, service, partner.Id, "THIS IS NOT X12 AT ALL");

        var result = await service.ProcessTransactionAsync(transaction.Id, CancellationToken.None);

        result.Status.Should().Be(EdiTransactionStatus.Error);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    // ─────────────────────────── Outbound ───────────────────────────

    [Fact]
    public async Task Generate855_RendersAcknowledgment_ForSalesOrder()
    {
        var (db, service, _) = CreateHarness();
        var (partner, customerId) = await SeedPartnerAsync(db);
        var salesOrder = new SalesOrder
        {
            OrderNumber = "SO-1001", CustomerId = customerId, CustomerPO = "PO-77821",
            Lines = { new SalesOrderLine { Description = "Widget", Quantity = 500m, UnitPrice = 2.5m, LineNumber = 1 } },
        };
        db.SalesOrders.Add(salesOrder);
        await db.SaveChangesAsync();

        var transaction = await service.GeneratePoAckAsync(salesOrder.Id, partner.Id, CancellationToken.None);

        transaction.TransactionSet.Should().Be("855");
        transaction.Direction.Should().Be(EdiDirection.Outbound);
        transaction.RawPayload.Should().Contain("ST*855*").And.Contain("BAK*00*AC*PO-77821");
        AssertValidEnvelope(transaction.RawPayload);
    }

    [Fact]
    public async Task Generate810_RendersInvoice_AndRoundTripsThroughEdiNet()
    {
        var (db, service, _) = CreateHarness();
        var (partner, customerId) = await SeedPartnerAsync(db);
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-0042", CustomerId = customerId,
            InvoiceDate = new DateTimeOffset(2026, 6, 13, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero),
            Lines = { new InvoiceLine { Description = "Widget", Quantity = 500m, UnitPrice = 2.5m, LineNumber = 1 } },
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var transaction = await service.GenerateInvoiceEdiAsync(invoice.Id, partner.Id, CancellationToken.None);

        transaction.TransactionSet.Should().Be("810");
        transaction.RawPayload.Should()
            .Contain("BIG*20260613*INV-0042")
            .And.Contain("IT1*1*500*EA*2.5")
            .And.Contain("TDS*125000"); // $1,250.00 in implied-decimal cents
        AssertValidEnvelope(transaction.RawPayload);

        // Round-trip: EDI.Net parses our own rendered envelope (writer ↔ parser agreement).
        using var reader = new StringReader(transaction.RawPayload);
        var parsed = new indice.Edi.EdiSerializer()
            .Deserialize<Edi850Interchange>(reader, indice.Edi.EdiGrammar.NewX12());
        parsed.SenderId!.Trim().Should().Be("FORGE");
        parsed.ReceiverId!.Trim().Should().Be("ACMEEDI");
        parsed.ControlNumber.Should().Be(1);
    }

    [Fact]
    public async Task SendTransaction_DelegatesToTransport()
    {
        var (db, service, transport) = CreateHarness();
        var (partner, customerId) = await SeedPartnerAsync(db);
        var salesOrder = new SalesOrder
        {
            OrderNumber = "SO-1002", CustomerId = customerId,
            Lines = { new SalesOrderLine { Description = "Widget", Quantity = 1m, UnitPrice = 1m, LineNumber = 1 } },
        };
        db.SalesOrders.Add(salesOrder);
        await db.SaveChangesAsync();
        var transaction = await service.GeneratePoAckAsync(salesOrder.Id, partner.Id, CancellationToken.None);
        db.EdiTransactions.Add(transaction);
        await db.SaveChangesAsync();

        await service.SendTransactionAsync(transaction.Id, CancellationToken.None);

        transport.Sent.Should().ContainSingle(p => p == transaction.RawPayload);
    }

    /// <summary>ISA fixed-width + ST/SE count + GE/IEA structural assertions.</summary>
    private static void AssertValidEnvelope(string payload)
    {
        var segments = payload.Replace("\n", string.Empty)
            .Split('~', StringSplitOptions.RemoveEmptyEntries);

        var isa = segments[0];
        isa.Should().StartWith("ISA*");
        // ISA is fixed-length: 16 elements; sender/receiver IDs padded to exactly 15.
        var isaElements = isa.Split('*');
        isaElements.Should().HaveCount(17);
        isaElements[6].Length.Should().Be(15);
        isaElements[8].Length.Should().Be(15);
        isaElements[13].Length.Should().Be(9); // control number D9

        segments[1].Should().StartWith("GS*");
        segments[2].Should().StartWith("ST*");
        segments[^2].Should().StartWith("GE*1*");
        segments[^1].Should().StartWith("IEA*1*");

        // SE01 counts ST..SE inclusive.
        var seIndex = Array.FindIndex(segments, s => s.StartsWith("SE*"));
        var declaredCount = int.Parse(segments[seIndex].Split('*')[1]);
        declaredCount.Should().Be(seIndex - 2 + 1); // segments between ST (index 2) and SE inclusive
    }
}
