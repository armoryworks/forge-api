using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
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
}
