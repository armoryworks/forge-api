using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// §7A dry run of the opening AR/AP open items: parties resolve by id, number or name (never by guessing
/// between two matches), amounts must be positive, due dates follow document dates, document numbers must be
/// free and unique, and the receivable/payable totals feed the tie-out. Nothing is written.
/// </summary>
public class OpeningOpenItemsValidationTests
{
    private const int BookId = 1;

    private static async Task<(AppDbContext Db, int CustomerId, int VendorId)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme Fabrication", CompanyName = "Acme Fabrication", CustomerNumber = "CUST-0001", IsActive = true };
        var twinA = new Customer { Name = "Twin Corp", IsActive = true };
        var twinB = new Customer { Name = "Twin Corp", IsActive = true };
        var vendor = new Vendor { CompanyName = "Steel Supply", VendorNumber = "VEND-0001", IsActive = true };
        db.Customers.AddRange(customer, twinA, twinB);
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        db.Invoices.Add(new Invoice
        {
            InvoiceNumber = "INV-TAKEN", CustomerId = customer.Id, Status = InvoiceStatus.Sent,
            InvoiceDate = DateTimeOffset.UtcNow, DueDate = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return (db, customer.Id, vendor.Id);
    }

    private static Task<OpeningOpenItemsValidationModel> ValidateAsync(AppDbContext db, params OpeningOpenItemRowModel[] rows)
        => new ValidateOpeningOpenItemsHandler(db).Handle(
            new ValidateOpeningOpenItemsQuery(new ValidateOpeningOpenItemsRequestModel(BookId, [.. rows])),
            CancellationToken.None);

    private static OpeningOpenItemRowModel Ar(string? partyRef, decimal amount, string? number = null, int? partyId = null,
        DateOnly? issued = null, DateOnly? due = null)
        => new(SubledgerPartyType.Customer, partyId, partyRef, number, issued, due, amount, null);

    private static OpeningOpenItemRowModel Ap(string? partyRef, decimal amount, string? number = null)
        => new(SubledgerPartyType.Vendor, null, partyRef, number, null, null, amount, null);

    [Fact]
    public async Task GoodRows_ResolvePartiesByNumberNameOrId_AndTotalBothSides()
    {
        var (db, customerId, vendorId) = await SeedAsync();
        using var _ = db;

        var result = await ValidateAsync(db,
            Ar("cust-0001", 1200m, "L-1001"),
            Ar("Acme Fabrication", 300m),
            Ar(null, 50m, partyId: customerId),
            Ap("Steel Supply", 400m, "V-77"));

        result.ErrorCount.Should().Be(0);
        result.AlreadyPosted.Should().BeFalse();
        result.ReceivablesTotal.Should().Be(1550m);
        result.PayablesTotal.Should().Be(400m);
        result.Rows.Take(3).Should().OnlyContain(r => r.PartyId == customerId && r.PartyName == "Acme Fabrication");
        result.Rows[3].PartyId.Should().Be(vendorId);
    }

    [Fact]
    public async Task UnknownAndAmbiguousParties_AreFlagged_NeverGuessed()
    {
        var (db, _, _) = await SeedAsync();
        using var __ = db;

        var result = await ValidateAsync(db, Ar("Nobody Inc", 10m), Ar("Twin Corp", 10m));

        result.Rows[0].Errors.Should().Equal(ValidateOpeningOpenItemsHandler.PartyNotFound);
        result.Rows[1].Errors.Should().Equal(ValidateOpeningOpenItemsHandler.PartyAmbiguous);
        result.Rows[1].PartyId.Should().BeNull();
    }

    [Fact]
    public async Task AmountDatesAndDocumentNumbers_AreChecked()
    {
        var (db, _, _) = await SeedAsync();
        using var __ = db;

        var result = await ValidateAsync(db,
            Ar("CUST-0001", 0m),
            Ar("CUST-0001", 10m, issued: new DateOnly(2026, 2, 1), due: new DateOnly(2026, 1, 1)),
            Ar("CUST-0001", 10m, "INV-TAKEN"),
            Ar("CUST-0001", 10m, "DUP-1"),
            Ar("CUST-0001", 10m, "DUP-1"),
            Ar("CUST-0001", 10m, new string('9', 21)));

        result.Rows[0].Errors.Should().Contain(ValidateOpeningOpenItemsHandler.AmountNotPositive);
        result.Rows[1].Errors.Should().Contain(ValidateOpeningOpenItemsHandler.DueBeforeDocument);
        result.Rows[2].Errors.Should().Contain(ValidateOpeningOpenItemsHandler.DocumentNumberInUse);
        result.Rows[3].Errors.Should().Contain(ValidateOpeningOpenItemsHandler.DocumentNumberDuplicate);
        result.Rows[4].Errors.Should().Contain(ValidateOpeningOpenItemsHandler.DocumentNumberDuplicate);
        result.Rows[5].Errors.Should().Contain(ValidateOpeningOpenItemsHandler.DocumentNumberTooLong);
    }

    [Fact]
    public async Task VendorDocumentNumbers_AreCheckedPerVendor()
    {
        var (db, _, vendorId) = await SeedAsync();
        using var __ = db;
        db.VendorBills.Add(new VendorBill
        {
            BillNumber = "BILL-1", VendorId = vendorId, VendorInvoiceNumber = "V-9", Status = VendorBillStatus.Approved,
            BillDate = DateTimeOffset.UtcNow, DueDate = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await ValidateAsync(db, Ap("VEND-0001", 25m, "V-9"), Ar("CUST-0001", 25m, "V-9"));

        result.Rows[0].Errors.Should().Equal(ValidateOpeningOpenItemsHandler.DocumentNumberInUse);
        result.Rows[1].Errors.Should().BeEmpty("a customer invoice number does not collide with a vendor's invoice number");
    }

    [Fact]
    public async Task AlreadyPostedOpeningEntry_IsReported()
    {
        var (db, _, _) = await SeedAsync();
        using var __ = db;
        db.JournalEntries.Add(new JournalEntry
        {
            BookId = BookId, EntryNumber = 1, EntryDate = new DateOnly(2026, 1, 1), Status = JournalEntryStatus.Posted,
            Source = JournalSource.Conversion, IdempotencyKey = $"{JournalSource.Conversion}:OpeningBalance:{BookId}",
        });
        await db.SaveChangesAsync();

        (await ValidateAsync(db, Ar("CUST-0001", 5m))).AlreadyPosted.Should().BeTrue();
    }
}
