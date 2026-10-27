using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Imports;
using Forge.Api.Services.Fx;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Imports;

[Collection(PostgresCollection.Name)]
public sealed class HistoricalInvoiceImportPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Imported_invoices_and_payments_post_no_journal_and_no_open_items()
    {
        await using var db = fixture.CreateContext();
        var functional = await new DocumentCurrencyResolver(db).GetFunctionalCurrencyIdAsync(CancellationToken.None);
        if (!await db.Set<Currency>().AnyAsync(c => c.Id == functional))
        {
            db.Set<Currency>().Add(new Currency { Id = functional, Code = $"F{Guid.NewGuid():N}"[..3].ToUpperInvariant(), Name = "Functional", Symbol = "$", IsBaseCurrency = true, IsActive = true });
            await db.SaveChangesAsync();
        }
        var customer = new Customer { Name = $"Hist {Guid.NewGuid():N}"[..20], CurrencyId = functional };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var journalsBefore = await db.JournalEntries.IgnoreQueryFilters().CountAsync();
        var openItemsBefore = await db.ArOpenItems.IgnoreQueryFilters().CountAsync();
        var number = $"HP{Guid.NewGuid():N}"[..14];

        var result = await ImportHistoricalInvoicesTests.Handler(db).Handle(new ImportHistoricalInvoicesCommand(
        [
            new InvoiceImportRowModel("r1", number, customer.Name, DateTimeOffset.UtcNow.AddDays(-90), DateTimeOffset.UtcNow.AddDays(-60),
                null, "Legacy work", 3m, 40m, null, 120m, 20m, null, null, "PO-9"),
        ], Commit: true), CancellationToken.None);

        result.Committed.Should().BeTrue();
        await using var check = fixture.CreateContext();
        (await check.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(journalsBefore);
        (await check.ArOpenItems.IgnoreQueryFilters().CountAsync()).Should().Be(openItemsBefore);
        var invoice = await check.Invoices.Include(i => i.Lines).Include(i => i.PaymentApplications)
            .SingleAsync(i => i.InvoiceNumber == number);
        invoice.BalanceDue.Should().Be(100m);
        invoice.CustomerPO.Should().Be("PO-9");
    }
}
