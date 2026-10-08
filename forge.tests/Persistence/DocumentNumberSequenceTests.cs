using FluentAssertions;

using Moq;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class DocumentNumberSequenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Today = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

    private static async Task<int> SeedCustomerAsync(AppDbContext db)
    {
        var customer = new Customer { Name = $"Numbering {Guid.NewGuid():N}"[..20] };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private static int Suffix(string number) => int.Parse(number[(number.IndexOf('-') + 1)..]);

    private static Invoice NewInvoice(int customerId, int currencyId, string number) => new()
    {
        InvoiceNumber = number,
        CustomerId = customerId,
        CurrencyId = currencyId,
        Status = InvoiceStatus.Draft,
        InvoiceDate = Today,
        DueDate = Today.AddDays(30),
    };

    private sealed record Series(
        string Prefix,
        Func<CancellationToken, Task<string>> Next,
        Func<string, DateTimeOffset?, Task> Add);

    private static async Task SaveAsync(AppDbContext db, object entity)
    {
        db.Add(entity);
        await db.SaveChangesAsync();
    }

    private static async Task<int> SeedVendorAsync(AppDbContext db)
    {
        var vendor = new Vendor { CompanyName = $"Numbering {Guid.NewGuid():N}"[..20] };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        return vendor.Id;
    }

    private static async Task<int> SeedCurrencyAsync(AppDbContext db)
    {
        var currency = new Currency { Code = $"N{Guid.NewGuid():N}"[..8], Name = "US Dollar", Symbol = "$" };
        db.Set<Currency>().Add(currency);
        await db.SaveChangesAsync();
        return currency.Id;
    }

    private static async Task<int> SeedSalesOrderAsync(AppDbContext db)
    {
        var order = new SalesOrder { OrderNumber = $"T{Guid.NewGuid():N}"[..12], CustomerId = await SeedCustomerAsync(db) };
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<Series> SeriesForAsync(AppDbContext db, string prefix)
    {
        switch (prefix)
        {
            case "PO":
            {
                var vendorId = await SeedVendorAsync(db);
                return new(prefix, new PurchaseOrderRepository(db).GenerateNextPONumberAsync,
                    (n, d) => SaveAsync(db, new PurchaseOrder { PONumber = n, VendorId = vendorId, DeletedAt = d }));
            }
            case "SH":
            {
                var orderId = await SeedSalesOrderAsync(db);
                return new(prefix, new ShipmentRepository(db).GenerateNextShipmentNumberAsync,
                    (n, d) => SaveAsync(db, new Shipment { ShipmentNumber = n, SalesOrderId = orderId, DeletedAt = d }));
            }
            case "VEND":
                return new(prefix, new VendorRepository(db).GenerateNextVendorNumberAsync,
                    (n, d) => SaveAsync(db, new Vendor { CompanyName = $"Numbering {Guid.NewGuid():N}"[..20], VendorNumber = n, DeletedAt = d }));
            case "CUST":
                return new(prefix, new CustomerRepository(db).GenerateNextCustomerNumberAsync,
                    (n, d) => SaveAsync(db, new Customer { Name = $"Numbering {Guid.NewGuid():N}"[..20], CustomerNumber = n, DeletedAt = d }));
            case "LEAD":
                return new(prefix, new LeadRepository(db).GenerateNextLeadNumberAsync,
                    (n, d) => SaveAsync(db, new Lead { CompanyName = $"Numbering {Guid.NewGuid():N}"[..20], LeadNumber = n, DeletedAt = d }));
            case "PMT":
            {
                var customerId = await SeedCustomerAsync(db);
                return new(prefix, new PaymentRepository(db).GenerateNextPaymentNumberAsync,
                    (n, d) => SaveAsync(db, new Payment
                    {
                        PaymentNumber = n,
                        CustomerId = customerId,
                        Method = PaymentMethod.Check,
                        Amount = 10m,
                        PaymentDate = Today,
                        DeletedAt = d,
                    }));
            }
            case "VPMT":
            {
                var vendorId = await SeedVendorAsync(db);
                return new(prefix, new VendorPaymentRepository(db).GenerateNextVendorPaymentNumberAsync,
                    (n, d) => SaveAsync(db, new VendorPayment
                    {
                        PaymentNumber = n,
                        VendorId = vendorId,
                        Method = PaymentMethod.Check,
                        Amount = 10m,
                        PaymentDate = Today,
                        DeletedAt = d,
                    }));
            }
            case "BILL":
            {
                var vendorId = await SeedVendorAsync(db);
                var currencyId = await SeedCurrencyAsync(db);
                return new(prefix, new VendorBillRepository(db).GenerateNextBillNumberAsync,
                    (n, d) => SaveAsync(db, new VendorBill
                    {
                        BillNumber = n,
                        VendorId = vendorId,
                        CurrencyId = currencyId,
                        BillDate = Today,
                        DueDate = Today.AddDays(30),
                        DeletedAt = d,
                    }));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(prefix), prefix, null);
        }
    }

    [Fact]
    public async Task Invoice_numbers_continue_past_a_manual_number()
    {
        await using var db = fixture.CreateContext();
        var repo = new InvoiceRepository(db);
        var customerId = await SeedCustomerAsync(db);
        var currency = new Currency { Code = $"N{Guid.NewGuid():N}"[..8], Name = "US Dollar", Symbol = "$" };
        db.Set<Currency>().Add(currency);
        await db.SaveChangesAsync();

        var next = await repo.GenerateNextInvoiceNumberAsync(CancellationToken.None);
        db.Invoices.Add(NewInvoice(customerId, currency.Id, next));
        await db.SaveChangesAsync();
        db.Invoices.Add(NewInvoice(customerId, currency.Id, "INV-RUSH"));
        await db.SaveChangesAsync();

        var after = await repo.GenerateNextInvoiceNumberAsync(CancellationToken.None);

        after.Should().StartWith("INV-");
        Suffix(after).Should().Be(Suffix(next) + 1);
    }

    [Fact]
    public async Task Quote_numbers_continue_past_a_manual_number()
    {
        await using var db = fixture.CreateContext();
        var repo = new QuoteRepository(db);
        var customerId = await SeedCustomerAsync(db);

        var next = await repo.GenerateNextQuoteNumberAsync(CancellationToken.None);
        db.Quotes.Add(new Quote { Type = QuoteType.Quote, QuoteNumber = next, CustomerId = customerId });
        await db.SaveChangesAsync();
        db.Quotes.Add(new Quote { Type = QuoteType.Quote, QuoteNumber = "QT-RUSH", CustomerId = customerId });
        await db.SaveChangesAsync();

        var after = await repo.GenerateNextQuoteNumberAsync(CancellationToken.None);

        after.Should().StartWith("QT-");
        Suffix(after).Should().Be(Suffix(next) + 1);
    }

    [Fact]
    public async Task Sales_order_numbers_continue_past_a_manual_number()
    {
        await using var db = fixture.CreateContext();
        var repo = new SalesOrderRepository(db);
        var customerId = await SeedCustomerAsync(db);

        var next = await repo.GenerateNextOrderNumberAsync(CancellationToken.None);
        db.SalesOrders.Add(new SalesOrder { OrderNumber = next, CustomerId = customerId });
        await db.SaveChangesAsync();
        db.SalesOrders.Add(new SalesOrder { OrderNumber = "SO-RUSH1", CustomerId = customerId });
        await db.SaveChangesAsync();

        var after = await repo.GenerateNextOrderNumberAsync(CancellationToken.None);

        after.Should().StartWith("SO-");
        Suffix(after).Should().Be(Suffix(next) + 1);
    }

    [Fact]
    public async Task Highest_suffix_wins_regardless_of_insert_order()
    {
        await using var db = fixture.CreateContext();
        var repo = new SalesOrderRepository(db);
        var customerId = await SeedCustomerAsync(db);
        var prefix = $"T{Guid.NewGuid():N}"[..7];

        foreach (var number in new[] { $"{prefix}-00007", $"{prefix}-00003", $"{prefix}-X9", $"{prefix}-RUSH1" })
        {
            db.SalesOrders.Add(new SalesOrder { OrderNumber = number, CustomerId = customerId });
            await db.SaveChangesAsync();
        }

        var after = await repo.GenerateNextOrderNumberAsync(prefix, CancellationToken.None);

        after.Should().Be($"{prefix}-00008");
    }

    [Fact]
    public async Task An_unused_prefix_starts_at_one()
    {
        await using var db = fixture.CreateContext();
        var repo = new SalesOrderRepository(db);

        var first = await repo.GenerateNextOrderNumberAsync($"N{Guid.NewGuid():N}"[..7], CancellationToken.None);

        first.Should().EndWith("-00001");
    }

    [Theory]
    [InlineData("PO", "PO-RUSH")]
    [InlineData("SH", "SH-RUSH")]
    [InlineData("VEND", "VEND-ACME")]
    [InlineData("CUST", "CUST-ACME")]
    [InlineData("LEAD", "LEAD-ACME")]
    [InlineData("PMT", "PMT-WIRE")]
    [InlineData("VPMT", "VPMT-WIRE")]
    [InlineData("BILL", "BILL-ACME")]
    public async Task Generator_skips_manual_and_legacy_numbers(string prefix, string manual)
    {
        await using var db = fixture.CreateContext();
        var series = await SeriesForAsync(db, prefix);

        var next = await series.Next(CancellationToken.None);
        await series.Add(next, null);
        await series.Add(manual, null);
        await series.Add($"{prefix}-2501N", null);

        var after = await series.Next(CancellationToken.None);

        after.Should().Be($"{prefix}-{Suffix(next) + 1:D5}");
    }

    [Theory]
    [InlineData("PO")]
    [InlineData("SH")]
    [InlineData("VEND")]
    [InlineData("CUST")]
    [InlineData("LEAD")]
    [InlineData("PMT")]
    [InlineData("VPMT")]
    [InlineData("BILL")]
    public async Task Generator_continues_from_the_highest_number_when_a_later_row_is_manual(string prefix)
    {
        await using var db = fixture.CreateContext();
        var series = await SeriesForAsync(db, prefix);
        var highest = Suffix(await series.Next(CancellationToken.None)) + 41;

        await series.Add($"{prefix}-{highest:D5}", null);
        await series.Add($"{prefix}-M{Guid.NewGuid():N}"[..(prefix.Length + 9)], null);

        var after = await series.Next(CancellationToken.None);

        after.Should().Be($"{prefix}-{highest + 1:D5}");
    }

    [Theory]
    [InlineData("PO")]
    [InlineData("SH")]
    [InlineData("VEND")]
    [InlineData("CUST")]
    [InlineData("LEAD")]
    [InlineData("PMT")]
    [InlineData("VPMT")]
    [InlineData("BILL")]
    public async Task Generator_counts_soft_deleted_rows(string prefix)
    {
        await using var db = fixture.CreateContext();
        var series = await SeriesForAsync(db, prefix);
        var deleted = Suffix(await series.Next(CancellationToken.None)) + 5;

        await series.Add($"{prefix}-{deleted:D5}", Today);

        var after = await series.Next(CancellationToken.None);

        after.Should().Be($"{prefix}-{deleted + 1:D5}");
    }

    [Fact]
    public async Task Rfq_award_numbers_the_po_in_the_po_series()
    {
        await using var db = fixture.CreateContext();
        var purchaseOrders = new PurchaseOrderRepository(db);
        var vendorId = await SeedVendorAsync(db);
        var part = new Part { PartNumber = $"RFQ-{Guid.NewGuid():N}"[..16], Description = "Numbering" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        var expected = await purchaseOrders.GenerateNextPONumberAsync(CancellationToken.None);
        db.PurchaseOrders.Add(new PurchaseOrder { PONumber = $"PO-{Today:yyyyMMdd}-001", VendorId = vendorId });
        var rfq = new RequestForQuote
        {
            RfqNumber = $"RFQ-{Guid.NewGuid():N}"[..20],
            PartId = part.Id,
            Quantity = 5m,
            RequiredDate = Today.AddDays(14),
            Status = RfqStatus.Sent,
        };
        var response = new RfqVendorResponse { VendorId = vendorId, ResponseStatus = RfqResponseStatus.Received, UnitPrice = 2m };
        rfq.VendorResponses.Add(response);
        db.RequestForQuotes.Add(rfq);
        await db.SaveChangesAsync();
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Today);

        var poId = await new RfqService(db, clock.Object, purchaseOrders)
            .AwardAndCreatePoAsync(rfq.Id, response.Id, CancellationToken.None);

        var po = await db.PurchaseOrders.FindAsync(poId);
        po!.PONumber.Should().Be(expected);
    }
}
