using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class InvoiceListRoundingTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Today = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

    private static async Task<(int CustomerId, int CurrencyId)> SeedAsync(AppDbContext db)
    {
        var customer = new Customer { Name = $"Rounding {Guid.NewGuid():N}"[..20] };
        var currency = new Currency { Code = $"R{Guid.NewGuid():N}"[..8], Name = "US Dollar", Symbol = "$" };
        db.Customers.Add(customer);
        db.Set<Currency>().Add(currency);
        await db.SaveChangesAsync();
        return (customer.Id, currency.Id);
    }

    private static async Task<InvoiceListItemModel> ListSingleAsync(AppDbContext db, int customerId, Invoice invoice)
    {
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var page = await new InvoiceRepository(db).GetPagedAsync(
            new InvoiceListQuery { CustomerId = customerId }, CancellationToken.None);
        return page.Items.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task List_total_rounds_a_half_cent_away_from_zero_like_the_invoice()
    {
        await using var db = fixture.CreateContext();
        var (customerId, currencyId) = await SeedAsync(db);
        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-R{Guid.NewGuid():N}"[..14],
            CustomerId = customerId,
            CurrencyId = currencyId,
            Status = InvoiceStatus.Draft,
            InvoiceDate = Today,
            DueDate = Today.AddDays(30),
            TaxRate = 0m,
            Lines = [new InvoiceLine { Description = "Half cent", Quantity = 3, UnitPrice = 33.335m, LineNumber = 1 }],
        };

        var row = await ListSingleAsync(db, customerId, invoice);

        row.Total.Should().Be(100.01m);
        row.Total.Should().Be(invoice.Total);
        row.BalanceDue.Should().Be(invoice.BalanceDue);
    }

    [Fact]
    public async Task List_total_matches_the_invoice_with_taxed_and_untaxed_lines()
    {
        await using var db = fixture.CreateContext();
        var (customerId, currencyId) = await SeedAsync(db);
        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-T{Guid.NewGuid():N}"[..14],
            CustomerId = customerId,
            CurrencyId = currencyId,
            Status = InvoiceStatus.Draft,
            InvoiceDate = Today,
            DueDate = Today.AddDays(30),
            TaxRate = 0.0725m,
            Lines =
            [
                new InvoiceLine { Description = "Taxed", Quantity = 3, UnitPrice = 33.335m, LineNumber = 1 },
                new InvoiceLine { Description = "Untaxed", Quantity = 0.1m, UnitPrice = 0.2m, LineNumber = 2, TaxCode = InvoiceLine.NonTaxableCode },
            ],
        };

        var row = await ListSingleAsync(db, customerId, invoice);

        row.Total.Should().Be(invoice.Total);
        row.BalanceDue.Should().Be(invoice.BalanceDue);
        decimal.Round(row.Total, 2).Should().Be(row.Total);
    }
}
