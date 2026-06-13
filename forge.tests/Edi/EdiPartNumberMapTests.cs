using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Edi;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Edi;

/// <summary>
/// EDI part-number translation (EDI_CORE_PLAN §Known functional gap → built). Proves:
///   • the map service round-trips typed rows and resolves each against the part catalog
///     (OurPartId set when our number exists, null when it doesn't);
///   • CSV import upserts by partner number (header synonyms + positional fallback) and reports
///     unresolved targets;
///   • a translated 850 line resolves to OUR part when the partner uses THEIR number;
///   • a mapping whose target part is missing still creates the line (fallback) with both numbers
///     in the notes; an unmapped number behaves exactly as before.
/// </summary>
public class EdiPartNumberMapTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 6, 13, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoopTransport : IEdiTransportService
    {
        public EdiTransportMethod Method => EdiTransportMethod.Manual;
        public Task SendAsync(string p, string c, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> PollAsync(string c, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<bool> TestConnectionAsync(string c, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class NoopFactory : IEdiTransportFactory
    {
        public IEdiTransportService For(EdiTransportMethod method) => new NoopTransport();
    }

    private static async Task<(EdiTradingPartner Partner, int CustomerId)> SeedPartnerAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();
        var partner = new EdiTradingPartner
        {
            Name = "Acme EDI", CustomerId = customer.Id,
            QualifierId = "ZZ", QualifierValue = "ACMEEDI",
            InterchangeSenderId = "ACMEEDI", InterchangeReceiverId = "FORGE",
            DefaultFormat = EdiFormat.X12, AutoProcess = true, IsActive = true,
        };
        db.EdiTradingPartners.Add(partner);
        await db.SaveChangesAsync();
        return (partner, customer.Id);
    }

    private static async Task<int> SeedPartAsync(AppDbContext db, string partNumber, string desc = "A part")
    {
        var part = new Part { PartNumber = partNumber, Description = desc };
        db.Set<Part>().Add(part);
        await db.SaveChangesAsync();
        return part.Id;
    }

    // ─────────────────────────── Map service ───────────────────────────

    [Fact]
    public async Task ReplaceRows_RoundTrips_AndResolvesAgainstCatalog()
    {
        var db = TestDbContextFactory.Create();
        var (partner, _) = await SeedPartnerAsync(db);
        var partId = await SeedPartAsync(db, "WIDGET-100");
        var service = new EdiPartNumberMapService(db);

        var saved = await service.ReplaceRowsAsync(partner.Id,
        [
            new EdiPartNumberMapRow { PartnerPartNumber = "ACME-X", OurPartNumber = "WIDGET-100" },
            new EdiPartNumberMapRow { PartnerPartNumber = "ACME-Y", OurPartNumber = "MISSING-1" },
            new EdiPartNumberMapRow { PartnerPartNumber = "", OurPartNumber = "BLANK" }, // dropped
        ]);

        saved.Should().HaveCount(2);
        saved.Single(r => r.PartnerPartNumber == "ACME-X").OurPartId.Should().Be(partId);
        saved.Single(r => r.PartnerPartNumber == "ACME-Y").OurPartId.Should().BeNull();

        // Persisted + reloads through the conventional EdiMapping row.
        var reloaded = await service.GetRowsAsync(partner.Id);
        reloaded.Should().HaveCount(2);
        (await db.EdiMappings.CountAsync(m => m.TradingPartnerId == partner.Id)).Should().Be(1);
    }

    [Fact]
    public async Task GetTranslation_IsCaseInsensitive()
    {
        var db = TestDbContextFactory.Create();
        var (partner, _) = await SeedPartnerAsync(db);
        var service = new EdiPartNumberMapService(db);
        await service.ReplaceRowsAsync(partner.Id,
            [new EdiPartNumberMapRow { PartnerPartNumber = "Acme-X", OurPartNumber = "WIDGET-100" }]);

        var map = await service.GetTranslationAsync(partner.Id);
        map.TryGetValue("ACME-X", out var ours).Should().BeTrue();
        ours.Should().Be("WIDGET-100");
    }

    [Fact]
    public async Task ImportCsv_UpsertsByPartnerNumber_ReportsUnresolved()
    {
        var db = TestDbContextFactory.Create();
        var (partner, _) = await SeedPartnerAsync(db);
        await SeedPartAsync(db, "WIDGET-100");
        var service = new EdiPartNumberMapService(db);
        await service.ReplaceRowsAsync(partner.Id,
            [new EdiPartNumberMapRow { PartnerPartNumber = "ACME-X", OurPartNumber = "OLD-1" }]);

        var csv = "Partner Part,Our Part\nACME-X,WIDGET-100\nACME-Z,MISSING-9\n";
        var result = await service.ImportCsvAsync(partner.Id, csv);

        result.Imported.Should().Be(1);   // ACME-Z new
        result.Updated.Should().Be(1);    // ACME-X repointed to WIDGET-100
        result.TotalRows.Should().Be(2);
        result.Unresolved.Should().Be(1); // MISSING-9 has no part

        var rows = await service.GetRowsAsync(partner.Id);
        rows.Single(r => r.PartnerPartNumber == "ACME-X").OurPartNumber.Should().Be("WIDGET-100");
    }

    [Fact]
    public async Task ImportCsv_PositionalFallback_NoHeaderSynonyms()
    {
        var db = TestDbContextFactory.Create();
        var (partner, _) = await SeedPartnerAsync(db);
        var service = new EdiPartNumberMapService(db);

        // Unrecognized headers → positional (col 0 = partner, col 1 = ours).
        var result = await service.ImportCsvAsync(partner.Id, "Foo,Bar\nP-1,OUR-1\n");
        result.Imported.Should().Be(1);
        (await service.GetTranslationAsync(partner.Id))["P-1"].Should().Be("OUR-1");
    }

    // ─────────────────────────── 850 integration ───────────────────────────

    private const string Golden850 =
        "ISA*00*          *00*          *ZZ*ACMEEDI        *ZZ*FORGE          *260613*0900*U*00401*000000042*0*P*>~\n"
        + "GS*PO*ACMEEDI*FORGE*20260613*0900*42*X*004010~\n"
        + "ST*850*0001~\n"
        + "BEG*00*SA*PO-MAP-1**20260613~\n"
        + "PO1*1*500*EA*2.5*PE*BP*ACME-X~\n"      // partner's own number — needs translation
        + "PID*F****Their widget~\n"
        + "CTT*1~\n"
        + "SE*6*0001~\n"
        + "GE*1*42~\n"
        + "IEA*1*000000042~\n";

    private static X12EdiService Service(AppDbContext db)
        => new(db, new SalesOrderRepository(db), new NoopFactory(), new EdiPartNumberMapService(db), new FakeClock());

    [Fact]
    public async Task Process_TranslatesPartnerNumber_ToOurPart()
    {
        var db = TestDbContextFactory.Create();
        var (partner, _) = await SeedPartnerAsync(db);
        var partId = await SeedPartAsync(db, "WIDGET-100", "Our widget");
        await new EdiPartNumberMapService(db).ReplaceRowsAsync(partner.Id,
            [new EdiPartNumberMapRow { PartnerPartNumber = "ACME-X", OurPartNumber = "WIDGET-100" }]);

        var service = Service(db);
        var txn = await service.ReceiveDocumentAsync(Golden850, partner.Id, CancellationToken.None);
        db.EdiTransactions.Add(txn);
        await db.SaveChangesAsync();
        await service.ProcessTransactionAsync(txn.Id, CancellationToken.None);

        var line = (await db.SalesOrders.Include(s => s.Lines).SingleAsync()).Lines.Single();
        line.PartId.Should().Be(partId);                 // resolved via the partner's number → ours
        line.Description.Should().Be("Our widget");
        line.Notes.Should().BeNull();
    }

    [Fact]
    public async Task Process_MappingTargetMissing_FallsBack_WithBothNumbersInNotes()
    {
        var db = TestDbContextFactory.Create();
        var (partner, _) = await SeedPartnerAsync(db);
        await new EdiPartNumberMapService(db).ReplaceRowsAsync(partner.Id,
            [new EdiPartNumberMapRow { PartnerPartNumber = "ACME-X", OurPartNumber = "WIDGET-404" }]);

        var service = Service(db);
        var txn = await service.ReceiveDocumentAsync(Golden850, partner.Id, CancellationToken.None);
        db.EdiTransactions.Add(txn);
        await db.SaveChangesAsync();
        await service.ProcessTransactionAsync(txn.Id, CancellationToken.None);

        var line = (await db.SalesOrders.Include(s => s.Lines).SingleAsync()).Lines.Single();
        line.PartId.Should().BeNull();                   // line still created — nothing lost
        line.Notes.Should().Contain("ACME-X").And.Contain("WIDGET-404");
    }

    [Fact]
    public async Task Process_NoMapping_BehavesAsBefore()
    {
        var db = TestDbContextFactory.Create();
        var (partner, _) = await SeedPartnerAsync(db);

        var service = Service(db);
        var txn = await service.ReceiveDocumentAsync(Golden850, partner.Id, CancellationToken.None);
        db.EdiTransactions.Add(txn);
        await db.SaveChangesAsync();
        await service.ProcessTransactionAsync(txn.Id, CancellationToken.None);

        var line = (await db.SalesOrders.Include(s => s.Lines).SingleAsync()).Lines.Single();
        line.PartId.Should().BeNull();
        line.Notes.Should().Contain("ACME-X");
        line.Notes.Should().NotContain("mapped to");      // no translation attempted
    }
}
