using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using MediatR;

using System.Security.Claims;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-2 STAGE C atomicity proof (the receipt twin of <see cref="Phase2ApHandlerAtomicityTests"/>):
/// the ReceiveItems handler must commit the receiving records + ReceivedQuantity increments + PO-status
/// flip AND the inventory/GRNI posting together — both or neither. Runs against real Postgres because the
/// InMemory provider ignores transactions. The "rolls back" test omits the GRNI determination rule so the
/// posting throws after the operational write, then asserts via a fresh context that nothing survived.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Phase2ReceiptHandlerAtomicityTests(PostgresFixture fixture)
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int InvRawId = 130;
    private const int GrniId = 210;
    private const int FreightId = 220;
    private const int OpExId = 600;

    private sealed class FakeCapabilities(bool fullGlOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = fullGlOn },
            DateTimeOffset.UtcNow);
        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static IHttpContextAccessor HttpContextFor(int userId)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
        };
        return new HttpContextAccessor { HttpContext = ctx };
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new AcctNumberSequenceAllocator(db), new SystemClock());

    private static ReceiveItemsHandler Handler(AppDbContext db)
        => new(new PurchaseOrderRepository(db), new SystemClock(), new Mock<IMediator>().Object, HttpContextFor(7),
            db, new ReceiptInventoryPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: true)));

    private static Task ResetAsync(AppDbContext db)
        => db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT tablename FROM pg_tables
            WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory') LOOP
    EXECUTE 'TRUNCATE TABLE ' || quote_ident(r.tablename) || ' RESTART IDENTITY CASCADE';
  END LOOP;
END $$;");

    private async Task SeedAccountingAsync(AppDbContext db, params string[] omitRuleKeys)
    {
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = InvRawId, BookId = BookId, AccountNumber = "13100", Name = "Inventory — Raw", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = GrniId, BookId = BookId, AccountNumber = "21000", Name = "GRNI", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FreightId, BookId = BookId, AccountNumber = "22000", Name = "Freight Clearing", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = OpExId, BookId = BookId, AccountNumber = "60000", Name = "G&A", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        var rules = new[] { ("INVENTORY_RAW", InvRawId), ("GRNI", GrniId), ("FREIGHT_CLEARING", FreightId), ("OPERATING_EXPENSE", OpExId) };
        foreach (var (key, accountId) in rules)
        {
            if (omitRuleKeys.Contains(key)) continue;
            db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = BookId, Key = key, GlAccountId = accountId });
        }
        await db.SaveChangesAsync();
    }

    private static async Task<(int poId, int lineId)> AddPoAsync(AppDbContext db)
    {
        var part = new Part { PartNumber = "P-RAW", Description = "x", InventoryClass = InventoryClass.Raw, ProcurementSource = ProcurementSource.Buy };
        db.Set<Part>().Add(part);
        // Seed the vendor too — real Postgres enforces the PO→Vendor FK (InMemory does not).
        var vendor = new Vendor { CompanyName = "Atomicity Vendor" };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-1", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();
        var line = new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = part.Id, OrderedQuantity = 10m, UnitPrice = 5m };
        db.Set<PurchaseOrderLine>().Add(line);
        await db.SaveChangesAsync();
        return (po.Id, line.Id);
    }

    [Fact]
    public async Task Receive_postingFailure_rollsBackTheReceipt()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAccountingAsync(db, omitRuleKeys: "GRNI"); // Cr GRNI leg fails to resolve
        var (poId, lineId) = await AddPoAsync(db);

        var act = async () => await Handler(db).Handle(
            new ReceiveItemsCommand(poId, [new ReceiveLineModel(lineId, 10m, null, null)]), CancellationToken.None);

        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("DETERMINATION_UNMAPPED");

        await using var verify = fixture.CreateContext();
        (await verify.Set<ReceivingRecord>().AnyAsync()).Should().BeFalse("the receipt must roll back");
        (await verify.Set<PurchaseOrderLine>().SingleAsync(l => l.Id == lineId)).ReceivedQuantity.Should().Be(0m);
        (await verify.Set<PurchaseOrder>().SingleAsync(p => p.Id == poId)).Status.Should().Be(PurchaseOrderStatus.Submitted);
        (await verify.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Receive_postingSucceeds_commitsReceiptAndJournal()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAccountingAsync(db);
        var (poId, lineId) = await AddPoAsync(db);

        await Handler(db).Handle(
            new ReceiveItemsCommand(poId, [new ReceiveLineModel(lineId, 10m, null, null)]), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        (await verify.Set<ReceivingRecord>().CountAsync()).Should().Be(1);
        (await verify.Set<PurchaseOrderLine>().SingleAsync(l => l.Id == lineId)).ReceivedQuantity.Should().Be(10m);
        var entry = await verify.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Source.Should().Be(JournalSource.Inventory);
        entry.Lines.Single(l => l.GlAccountId == InvRawId).Debit.Should().Be(50m);
        entry.Lines.Single(l => l.GlAccountId == GrniId).Credit.Should().Be(50m);
    }
}
