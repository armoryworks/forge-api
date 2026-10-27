using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Imports;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Imports;

public class ImportHistoricalInvoicesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    internal static ImportHistoricalInvoicesHandler Handler(AppDbContext db, DateTimeOffset? now = null, bool fullGl = false)
    {
        var capabilities = new Mock<ICapabilitySnapshotProvider>();
        capabilities.Setup(c => c.IsEnabled("CAP-ACCT-FULLGL")).Returns(fullGl);
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(now ?? Now);
        var identifiers = new Mock<IBusinessIdentifierService>();
        identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        return new ImportHistoricalInvoicesHandler(db, new PaymentRepository(db), identifiers.Object, clock.Object, capabilities.Object);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$", IsBaseCurrency = true, IsActive = true });
        db.Customers.Add(new Customer { Name = "Acme", CustomerNumber = "C-100" });
        db.Parts.Add(new Part { PartNumber = "BRK-1", Name = "Bracket" });
        await db.SaveChangesAsync();
        db.Invoices.Add(new Invoice
        {
            InvoiceNumber = "INV-OLD", CustomerId = db.Customers.Single().Id, CurrencyId = 1,
            InvoiceDate = Now, DueDate = Now,
        });
        await db.SaveChangesAsync();
    }

    private static InvoiceImportRowModel Line(string? number, string? description, decimal? qty, decimal? price,
        string? customer = "C-100", decimal? tax = null, decimal? total = null, decimal? paid = null, string? status = null,
        DateTimeOffset? due = null) =>
        new($"r{number}{description}", number, customer, Now.AddDays(-60), due ?? Now.AddDays(-30), null, description, qty, price,
            tax, total, paid, null, status, null);

    [Fact]
    public async Task Rows_sharing_a_number_become_one_invoice_with_lines_and_tax()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        var result = await Handler(db).Handle(new ImportHistoricalInvoicesCommand(
        [
            Line("H-1", "Brackets", 10m, 5m, tax: 4m, total: 104m),
            Line("H-1", "Setup", 1m, 50m, tax: 4m, total: 104m),
        ], Commit: true), CancellationToken.None);

        result.Committed.Should().BeTrue();
        result.CreatedCount.Should().Be(1);
        var invoice = await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.InvoiceNumber == "H-1");
        invoice.Lines.Should().HaveCount(2);
        invoice.Total.Should().Be(104m);
        invoice.Status.Should().Be(InvoiceStatus.Overdue);
        invoice.Provider.Should().Be(ImportHistoricalInvoicesHandler.Provider);
    }

    [Fact]
    public async Task Paid_amount_becomes_an_applied_payment_and_closes_the_invoice()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        await Handler(db).Handle(new ImportHistoricalInvoicesCommand(
            [Line("H-2", null, null, null, total: 250m, paid: 250m)], Commit: true), CancellationToken.None);

        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.PaymentApplications).SingleAsync(i => i.InvoiceNumber == "H-2");
        invoice.Status.Should().Be(InvoiceStatus.Paid);
        invoice.Lines.Should().ContainSingle().Which.UnitPrice.Should().Be(250m);
        invoice.PaymentApplications.Should().ContainSingle().Which.Amount.Should().Be(250m);
        invoice.BalanceDue.Should().Be(0m);
        (await db.Payments.SingleAsync()).Method.Should().Be(PaymentMethod.Other);
    }

    [Fact]
    public async Task Partial_payment_leaves_an_open_balance()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        await Handler(db).Handle(new ImportHistoricalInvoicesCommand(
            [Line("H-3", "Parts", 2m, 100m, paid: 50m, due: Now.AddDays(10))], Commit: true), CancellationToken.None);

        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.PaymentApplications).SingleAsync(i => i.InvoiceNumber == "H-3");
        invoice.Status.Should().Be(InvoiceStatus.PartiallyPaid);
        invoice.BalanceDue.Should().Be(150m);
    }

    [Fact]
    public async Task Full_gl_rejects_open_invoices_and_imports_closed_history()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        var rejected = await Handler(db, fullGl: true).Handle(new ImportHistoricalInvoicesCommand(
        [
            Line("H-20", "Open", 1m, 100m),
            Line("H-21", "Part paid", 1m, 100m, paid: 40m),
            Line("H-22", "Paid", 1m, 100m, paid: 100m),
            Line("H-23", "Void", 1m, 100m, status: "Voided"),
        ], Commit: true), CancellationToken.None);

        rejected.Committed.Should().BeFalse();
        rejected.Rows.Single(r => r.Number == "H-20").Errors.Should().Contain(e => e.Contains("opening receivables"));
        rejected.Rows.Single(r => r.Number == "H-21").Errors.Should().Contain(e => e.Contains("opening receivables"));
        rejected.Rows.Single(r => r.Number == "H-22").Errors.Should().BeEmpty();
        rejected.Rows.Single(r => r.Number == "H-23").Errors.Should().BeEmpty();

        var closed = await Handler(db, fullGl: true).Handle(new ImportHistoricalInvoicesCommand(
        [
            Line("H-22", "Paid", 1m, 100m, paid: 100m),
            Line("H-23", "Void", 1m, 100m, status: "Voided"),
        ], Commit: true), CancellationToken.None);

        closed.Committed.Should().BeTrue();
        closed.CreatedCount.Should().Be(2);
    }

    [Fact]
    public async Task Duplicate_and_unknown_customer_rows_are_errors_and_nothing_commits()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        var result = await Handler(db).Handle(new ImportHistoricalInvoicesCommand(
        [
            Line("INV-OLD", "Dup", 1m, 10m),
            Line("H-4", "Who", 1m, 10m, customer: "Stranger LLC"),
            Line("H-5", "Fine", 1m, 10m),
        ], Commit: true), CancellationToken.None);

        result.Committed.Should().BeFalse();
        result.InvalidCount.Should().Be(2);
        result.Rows.Single(r => r.Number == "INV-OLD").Errors.Should().Contain(e => e.Contains("already in use"));
        result.Rows.Single(r => r.Number == "H-4").Errors.Should().Contain(e => e.Contains("Unknown customer"));
        (await db.Invoices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Total_that_does_not_match_the_lines_is_an_error()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        var result = await Handler(db).Handle(new ImportHistoricalInvoicesCommand(
            [Line("H-6", "Parts", 2m, 100m, tax: 10m, total: 999m)], Commit: false), CancellationToken.None);

        result.Rows.Single().Errors.Should().Contain(e => e.Contains("does not match"));
    }

    [Fact]
    public async Task Rows_without_a_number_or_with_conflicting_headers_are_errors()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        var result = await Handler(db).Handle(new ImportHistoricalInvoicesCommand(
        [
            Line(null, "No number", 1m, 1m),
            Line("H-7", "A", 1m, 1m, tax: 0m),
            Line("H-7", "B", 1m, 1m, tax: 5m),
        ], Commit: false), CancellationToken.None);

        result.Rows.Single(r => r.Number is null).Errors.Should().Contain("Invoice number is required.");
        result.Rows.Where(r => r.Number == "H-7").Should().OnlyContain(r => r.Status == "Invalid");
    }

    [Fact]
    public async Task Tax_that_no_rate_reproduces_is_kept_as_a_non_taxable_line()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        await Handler(db).Handle(new ImportHistoricalInvoicesCommand(
            [Line("H-8", "Big order", 1m, 1_234_567.89m, tax: 98_765.47m, total: 1_333_333.36m)], Commit: true), CancellationToken.None);

        var invoice = await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.InvoiceNumber == "H-8");
        invoice.Total.Should().Be(1_333_333.36m);
        invoice.TaxRate.Should().Be(0m);
        invoice.Lines.Should().Contain(l => l.TaxCode == InvoiceLine.NonTaxableCode && l.UnitPrice == 98_765.47m);
    }
}
