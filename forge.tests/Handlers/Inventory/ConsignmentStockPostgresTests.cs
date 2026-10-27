using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Data.Services;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

/// <summary>
/// Inbound consignment against real Postgres: a receipt puts vendor-owned stock in a bin with no valuation and no
/// journal, and ATP leaves it out of owned on-hand; consuming it values it at the agreed price and posts Dr inventory /
/// Cr consignment liability; reconciling drafts a vendor bill that clears the liability once approved.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConsignmentStockPostgresTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
    }

    private const int LiabilityId = 211;
    private const int ApId = 200;
    private const string FullGl = "CAP-ACCT-FULLGL";

    private static ConsignmentStockService Service(AppDbContext db) => new(
        db, new InventoryRepository(db), new VendorBillRepository(db), InventoryLedgerTestBook.Engine(db),
        new InventoryValuationService(db), new StubCapabilitySnapshotProvider(FullGl, "CAP-P2P-BILL"), new SystemClock());

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

    private async Task<(int AgreementId, int PartId, int LocationId)> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await InventoryLedgerTestBook.SeedAsync(db);
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = LiabilityId, BookId = InventoryLedgerTestBook.BookId, AccountNumber = "21100", Name = "Consignment Liability", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ApId, BookId = InventoryLedgerTestBook.BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = InventoryLedgerTestBook.BookId, Key = "CONSIGNMENT_LIABILITY", GlAccountId = LiabilityId },
            new AccountDeterminationRule { BookId = InventoryLedgerTestBook.BookId, Key = "AP_CONTROL", GlAccountId = ApId });

        var part = new Part { PartNumber = "CN-BOLT", Name = "Bolt", InventoryClass = InventoryClass.Raw };
        var vendor = new Vendor { CompanyName = "Bolt Supply" };
        var location = new StorageLocation { Name = "Line side", LocationType = LocationType.Bin, IsActive = true };
        db.AddRange(part, vendor, location);
        await db.SaveChangesAsync();
        var agreement = new ConsignmentAgreement
        {
            Direction = ConsignmentDirection.Inbound, VendorId = vendor.Id, PartId = part.Id, AgreedUnitPrice = 2.5m,
            EffectiveFrom = new DateOnly(2026, 1, 1), MinStockQuantity = 20m,
        };
        db.Add(agreement);
        await db.SaveChangesAsync();
        return (agreement.Id, part.Id, location.Id);
    }

    [Fact]
    public async Task Receipt_holds_vendor_stock_without_valuation_and_outside_owned_on_hand()
    {
        var (agreementId, partId, locationId) = await SeedAsync();
        await using var db = fixture.CreateContext();

        var receipt = await Service(db).ReceiveAsync(agreementId,
            new RecordConsignmentTransactionRequestModel { Quantity = 100m, StorageLocationId = locationId, LotNumber = "L7" }, 1);

        receipt.Type.Should().Be(ConsignmentTransactionType.Receipt);
        receipt.StorageLocationName.Should().Be("Line side");
        var row = await db.BinContents.SingleAsync(c => c.EntityId == partId);
        row.OwnerVendorId.Should().NotBeNull();
        row.ConsignmentAgreementId.Should().Be(agreementId);
        row.Quantity.Should().Be(100m);
        (await db.InventoryValuations.AnyAsync(v => v.PartId == partId)).Should().BeFalse();
        (await db.JournalEntries.CountAsync()).Should().Be(0);
        (await db.BinMovements.SingleAsync()).ConsignmentAgreementId.Should().Be(agreementId);

        var atp = await new AtpService(db, new SystemClock()).CalculateAtpAsync(partId, 1m);
        atp.OnHand.Should().Be(0m);
        atp.ConsignedOnHand.Should().Be(100m);
        atp.CanFulfill.Should().BeFalse();
    }

    [Fact]
    public async Task Consumption_values_the_stock_and_the_bill_clears_the_liability()
    {
        var (agreementId, partId, locationId) = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            await Service(db).ReceiveAsync(agreementId,
                new RecordConsignmentTransactionRequestModel { Quantity = 100m, StorageLocationId = locationId }, 1);
        }

        await using (var db = fixture.CreateContext())
        {
            var (transaction, owned) = await Service(db).TakeOwnershipAsync(agreementId, 40m, null, null, null, "Line use", 1);
            transaction.JournalEntryId.Should().NotBeNull();
            owned.Should().ContainSingle().Which.OwnerVendorId.Should().BeNull();
        }

        await using (var db = fixture.CreateContext())
        {
            var rows = await db.BinContents.Where(c => c.EntityId == partId && c.RemovedAt == null).ToListAsync();
            rows.Single(r => r.OwnerVendorId != null).Quantity.Should().Be(60m);
            rows.Single(r => r.OwnerVendorId == null).Quantity.Should().Be(40m);
            var store = await db.InventoryValuations.SingleAsync(v => v.PartId == partId);
            store.OnHandQuantity.Should().Be(40m);
            store.TotalValue.Should().Be(100m);

            var tx = await db.ConsignmentTransactions.SingleAsync(t => t.Type == ConsignmentTransactionType.Consumption);
            var lines = await InventoryLedgerTestBook.LinesAsync(db, $"Inventory:{ConsignmentStockService.SourceType}:{tx.Id}:CONSUME");
            lines.Single(l => l.GlAccountId == InventoryLedgerTestBook.RawId).Debit.Should().Be(100m);
            lines.Single(l => l.GlAccountId == LiabilityId).Credit.Should().Be(100m);

            var atp = await new AtpService(db, new SystemClock()).CalculateAtpAsync(partId, 1m);
            atp.OnHand.Should().Be(40m);
            atp.ConsignedOnHand.Should().Be(60m);
        }

        int billId;
        await using (var db = fixture.CreateContext())
        {
            var reconciliation = await Service(db).ReconcileAsync(agreementId, new ReconcileConsignmentRequestModel { VendorInvoiceNumber = "INV-77" }, 1);
            reconciliation.ConsumedQuantity.Should().Be(40m);
            reconciliation.ConsumedAmount.Should().Be(100m);
            billId = reconciliation.VendorBillId!.Value;
            reconciliation.VendorBillNumber.Should().NotBeNullOrEmpty();
        }

        await using (var db = fixture.CreateContext())
        {
            var bill = await db.VendorBills.SingleAsync(b => b.Id == billId);
            bill.Status = VendorBillStatus.Approved;
            await db.SaveChangesAsync();
            await new VendorBillApPostingService(db, InventoryLedgerTestBook.Engine(db), new StubCapabilitySnapshotProvider(FullGl))
                .PostVendorBillApprovedAsync(billId, 2);
        }

        await using (var db = fixture.CreateContext())
        {
            var liability = await db.JournalLines.Where(l => l.GlAccountId == LiabilityId).ToListAsync();
            liability.Sum(l => l.Credit - l.Debit).Should().Be(0m);
            (await db.JournalLines.Where(l => l.GlAccountId == ApId).SumAsync(l => l.Credit - l.Debit)).Should().Be(100m);
            (await db.ConsignmentTransactions.SingleAsync(t => t.Type == ConsignmentTransactionType.Consumption)).VendorBillId.Should().Be(billId);

            var summary = await new GetConsignmentStockSummaryHandler(db).Handle(new GetConsignmentStockSummaryQuery(null, null), CancellationToken.None);
            var row = summary.Agreements.Single();
            row.OnHandQuantity.Should().Be(60m);
            row.OnHandValue.Should().Be(150m);
            row.UnbilledQuantity.Should().Be(0m);
            row.BelowMinimum.Should().BeFalse();
        }
    }
}
