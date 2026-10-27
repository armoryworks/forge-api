using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Inventory;
using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

/// <summary>
/// Issuing to a job straight from a consigned bin takes the issued quantity into ownership first (valued at the
/// agreed price, liability accrued) and then issues company stock to the job, so WIP carries the cost.
/// </summary>
public class ConsignmentMaterialIssueTests
{
    private static ConsignmentStockService Consignment(AppDbContext db, StubCapabilitySnapshotProvider caps) => new(
        db, new InventoryRepository(db), new VendorBillRepository(db), InventoryLedgerTestBook.Engine(db),
        new InventoryValuationService(db), caps, new SystemClock());

    [Fact]
    public async Task Issue_from_a_consigned_bin_consumes_it_and_charges_the_job()
    {
        using var db = TestDbContextFactory.Create();
        await InventoryLedgerTestBook.SeedAsync(db);
        db.Set<Forge.Core.Entities.Accounting.GlAccount>().Add(new Forge.Core.Entities.Accounting.GlAccount
        {
            Id = 211, BookId = InventoryLedgerTestBook.BookId, AccountNumber = "21100", Name = "Consignment Liability",
            AccountType = Forge.Core.Enums.Accounting.AccountType.Liability, NormalBalance = Forge.Core.Enums.Accounting.NormalBalance.Credit,
            IsPostable = true, IsActive = true,
        });
        db.Set<Forge.Core.Entities.Accounting.AccountDeterminationRule>().Add(new Forge.Core.Entities.Accounting.AccountDeterminationRule
        {
            BookId = InventoryLedgerTestBook.BookId, Key = "CONSIGNMENT_LIABILITY", GlAccountId = 211,
        });
        var part = new Part { PartNumber = "CN-WASHER", Name = "Washer", InventoryClass = InventoryClass.Raw };
        var vendor = new Vendor { CompanyName = "Washer Supply" };
        var location = new StorageLocation { Name = "Line side", LocationType = LocationType.Bin, IsActive = true };
        var job = new Job { JobNumber = "J-CN-1", Title = "Consigned issue" };
        db.AddRange(part, vendor, location, job);
        await db.SaveChangesAsync();
        var agreement = new ConsignmentAgreement
        {
            Direction = ConsignmentDirection.Inbound, VendorId = vendor.Id, PartId = part.Id, AgreedUnitPrice = 1.25m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        };
        db.Add(agreement);
        await db.SaveChangesAsync();

        var caps = new StubCapabilitySnapshotProvider(InventoryLedgerTestBook.FullGl);
        var consignment = Consignment(db, caps);
        await consignment.ReceiveAsync(agreement.Id, new RecordConsignmentTransactionRequestModel { Quantity = 50m, StorageLocationId = location.Id }, 1);
        var consignedRow = await db.BinContents.SingleAsync(c => c.OwnerVendorId != null);

        var posting = new MaterialIssuePostingService(db, InventoryLedgerTestBook.Engine(db), caps, null, new InventoryValuationService(db));
        var issued = await new CreateMaterialIssueHandler(db, posting, new SystemClock(), consignment).Handle(
            new CreateMaterialIssueCommand(job.Id, part.Id, null, 20m, consignedRow.Id, null, null, MaterialIssueType.Issue, null, 1),
            CancellationToken.None);

        issued.Quantity.Should().Be(20m);
        consignedRow.Quantity.Should().Be(30m);
        var consumption = await db.ConsignmentTransactions.SingleAsync(t => t.Type == ConsignmentTransactionType.Consumption);
        consumption.JobId.Should().Be(job.Id);
        consumption.ExtendedAmount.Should().Be(25m);
        (await db.BinContents.Where(c => c.OwnerVendorId == null && c.RemovedAt == null).SumAsync(c => c.Quantity)).Should().Be(0m);
        var wip = await db.JournalLines.Where(l => l.GlAccountId == InventoryLedgerTestBook.WipId).SumAsync(l => l.Debit - l.Credit);
        wip.Should().Be(25m);
        (await db.JournalLines.Where(l => l.GlAccountId == 211).SumAsync(l => l.Credit - l.Debit)).Should().Be(25m);
    }
}
