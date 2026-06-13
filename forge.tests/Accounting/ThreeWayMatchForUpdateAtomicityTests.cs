using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;
using Forge.Api.Features.VendorBills;

namespace Forge.Tests.Accounting;

/// <summary>
/// Real-Postgres race proof for the 3-way-match over-bill guard (Hardening H3). Two concurrent
/// <see cref="ApproveVendorBillHandler"/> calls bill 4 each against a PO line with only 5 received: the
/// <c>FOR UPDATE</c> row lock must serialize them so the loser observes the winner's committed
/// <c>BilledQuantity</c> and fails the guard — never the lost-update where both pass and BilledQuantity
/// ends at 8 &gt; received. The lock is Npgsql-only, so InMemory tests cannot prove this.
/// (FULLGL stays OFF — the guard + lock are OPERATIONAL and run regardless; no GL seed needed.)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ThreeWayMatchForUpdateAtomicityTests(PostgresFixture fixture)
{
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

    /// <summary>Vendor + part + PO line (5 received, 0 billed) + two Draft bills billing 4 each against it.</summary>
    private static async Task<(int bill1Id, int bill2Id, int poLineId)> SeedAsync(AppDbContext db)
    {
        db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        var vendor = new Vendor { CompanyName = "Race Vendor" };
        var part = new Part
        {
            PartNumber = "P-RACE", Description = "x",
            InventoryClass = InventoryClass.Raw, ProcurementSource = ProcurementSource.Buy,
        };
        db.Set<Vendor>().Add(vendor);
        db.Set<Part>().Add(part);
        await db.SaveChangesAsync();

        var po = new PurchaseOrder { PONumber = "PO-RACE", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();
        var poLine = new PurchaseOrderLine
        {
            PurchaseOrderId = po.Id, PartId = part.Id,
            OrderedQuantity = 5m, ReceivedQuantity = 5m, UnitPrice = 10m,
        };
        db.Set<PurchaseOrderLine>().Add(poLine);
        await db.SaveChangesAsync();

        VendorBill MakeBill(string number) => new()
        {
            BillNumber = number, VendorId = vendor.Id, PurchaseOrderId = po.Id,
            CurrencyId = 1, FxRate = 1m,
            BillDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
            TaxAmount = 0m,
            Lines =
            [
                new VendorBillLine
                {
                    PartId = part.Id, PurchaseOrderLineId = poLine.Id,
                    Description = "Race line", Quantity = 4m, UnitPrice = 10m, LineNumber = 1,
                },
            ],
        };

        var b1 = MakeBill("BILL-RACE-1");
        var b2 = MakeBill("BILL-RACE-2");
        db.Set<VendorBill>().AddRange(b1, b2);
        await db.SaveChangesAsync();
        return (b1.Id, b2.Id, poLine.Id);
    }

    [Fact]
    public async Task ConcurrentApprovals_SerializeOnTheRowLock_ExactlyOneWins()
    {
        await using (var seedDb = fixture.CreateContext())
        {
            await ResetAsync(seedDb);
        }

        int bill1Id, bill2Id, poLineId;
        await using (var seedDb = fixture.CreateContext())
        {
            (bill1Id, bill2Id, poLineId) = await SeedAsync(seedDb);
        }

        // Two INDEPENDENT contexts/connections, fired together. The FOR UPDATE on purchase_order_lines makes
        // the second approval block until the first commits, then observe BilledQuantity = 4 → guard fails.
        await using var dbA = fixture.CreateContext();
        await using var dbB = fixture.CreateContext();
        var handlerA = new ApproveVendorBillHandler(new VendorBillRepository(dbA), db: dbA);
        var handlerB = new ApproveVendorBillHandler(new VendorBillRepository(dbB), db: dbB);

        var taskA = Task.Run(() => handlerA.Handle(new ApproveVendorBillCommand(bill1Id), CancellationToken.None));
        var taskB = Task.Run(() => handlerB.Handle(new ApproveVendorBillCommand(bill2Id), CancellationToken.None));

        var results = await Task.WhenAll(
            Capture(taskA),
            Capture(taskB));

        // Exactly one approval wins; the loser fails the over-bill guard (4 > 5 − 4 = 1 remaining).
        var failures = results.Where(e => e is not null).ToList();
        failures.Should().HaveCount(1, "the row lock must serialize the approvals so exactly one over-bills");
        failures[0].Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("received-but-not-yet-billed");

        // The committed state never exceeds what was received.
        await using var verify = fixture.CreateContext();
        (await verify.Set<PurchaseOrderLine>().SingleAsync(l => l.Id == poLineId))
            .BilledQuantity.Should().Be(4m, "only the winning bill may advance BilledQuantity");
        var statuses = await verify.Set<VendorBill>()
            .Where(b => b.Id == bill1Id || b.Id == bill2Id)
            .Select(b => b.Status)
            .ToListAsync();
        statuses.Should().Contain(VendorBillStatus.Approved);
        statuses.Should().Contain(VendorBillStatus.Draft, "the losing approval rolls back to Draft");
    }

    private static async Task<Exception?> Capture(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
