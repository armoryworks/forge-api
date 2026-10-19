using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Data;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// The AR origination posts cents: the receivable equals the invoice's quantized balance and its open item,
/// with the tax as the remainder, so a half-cent of tax never leaves a residual in AR control that blocks the
/// AR-ties check of the hard close.
/// </summary>
public class InvoiceArPostingRoundingTests
{
    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private sealed class FullGlOn : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = true },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static Invoice InvoiceOf(decimal taxRate, bool exempt, params (decimal Qty, decimal Price)[] lines)
    {
        var invoice = new Invoice { TaxRate = taxRate, Customer = new Customer { IsTaxExempt = exempt } };
        var n = 0;
        foreach (var (qty, price) in lines)
            invoice.Lines.Add(new InvoiceLine { LineNumber = ++n, Quantity = qty, UnitPrice = price, Description = $"line {n}" });
        return invoice;
    }

    [Fact]
    public void HalfCentTax_RoundsTheReceivableLikeBalanceDue()
    {
        var invoice = InvoiceOf(0.0725m, false, (1m, 12998m));

        var posted = InvoiceArPostingService.ComputePostedAmounts(invoice);

        posted.ArTotal.Should().Be(13940.36m);
        posted.TaxAmount.Should().Be(942.36m);
        posted.Lines.Single().Amount.Should().Be(12998m);
        posted.ArTotal.Should().Be(invoice.BalanceDue);
    }

    [Fact]
    public void FractionalLines_SumToTheRoundedSubtotal()
    {
        var invoice = InvoiceOf(0.0725m, false, (1m, 33.333m), (1m, 33.333m), (1m, 33.333m));

        var posted = InvoiceArPostingService.ComputePostedAmounts(invoice);

        posted.Lines.Sum(l => l.Amount).Should().Be(100.00m);
        posted.Lines.Should().OnlyContain(l => l.Amount == decimal.Round(l.Amount, 2));
        (posted.Lines.Sum(l => l.Amount) + posted.TaxAmount).Should().Be(posted.ArTotal);
        posted.ArTotal.Should().Be(invoice.BalanceDue);
    }

    [Fact]
    public void TaxExemptCustomer_InvoiceStoredAtZeroRate_PostsNoTax()
    {
        var invoice = InvoiceOf(0m, true, (3m, 10.005m));

        var posted = InvoiceArPostingService.ComputePostedAmounts(invoice);

        posted.TaxAmount.Should().Be(0m);
        posted.ArTotal.Should().Be(30.02m);
        posted.Lines.Single().Amount.Should().Be(30.02m);
        posted.ArTotal.Should().Be(invoice.BalanceDue);
    }

    [Fact]
    public void NonTaxableLine_PostsTaxOnTheTaxableLinesOnly()
    {
        var invoice = InvoiceOf(0.10m, false, (1m, 100m), (1m, 50m));
        invoice.Lines.Last().TaxCode = InvoiceLine.NonTaxableCode;

        var posted = InvoiceArPostingService.ComputePostedAmounts(invoice);

        posted.TaxAmount.Should().Be(10m);
        posted.ArTotal.Should().Be(160m);
        posted.ArTotal.Should().Be(invoice.BalanceDue);
    }

    [Fact]
    public async Task PostedInvoice_JournalOpenItemAndBalanceDueAgreeToTheCent()
    {
        using var db = TestDbContextFactory.Create();
        await SeedData.SeedAccountingAsync(db);
        var book = await db.Books.SingleAsync();
        var customer = new Customer { Name = "Customer A", IsActive = true };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-00001", CustomerId = customer.Id, CurrencyId = book.FunctionalCurrencyId,
            Status = InvoiceStatus.Sent, InvoiceDate = DateTimeOffset.UtcNow, DueDate = DateTimeOffset.UtcNow.AddDays(30),
            TaxRate = 0.0725m,
            Lines = { new InvoiceLine { LineNumber = 1, Quantity = 1m, UnitPrice = 12998m, Description = "Molded part" } },
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        var engine = new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

        await new InvoiceArPostingService(db, engine, new FullGlOn()).PostInvoiceFinalizedAsync(invoice.Id, 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.SourceType == "Invoice");
        var arAccountId = await db.AccountDeterminationRules.Where(r => r.Key == "AR_CONTROL").Select(r => r.GlAccountId).SingleAsync();
        var taxAccountId = await db.AccountDeterminationRules.Where(r => r.Key == "SALES_TAX_PAYABLE").Select(r => r.GlAccountId).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == arAccountId).FunctionalAmount.Should().Be(13940.36m);
        entry.Lines.Single(l => l.GlAccountId == taxAccountId).Credit.Should().Be(942.36m);
        (await db.ArOpenItems.SingleAsync()).OriginalFunctionalAmount.Should().Be(13940.36m);
        (await db.Invoices.Include(i => i.Lines).Include(i => i.PaymentApplications).SingleAsync()).BalanceDue.Should().Be(13940.36m);
    }
}
