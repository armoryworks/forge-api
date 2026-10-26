using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting.CreditMemos;

/// <summary>
/// A revaluation run after the period ends measures a foreign credit memo as it stood on the as-of date: applications
/// and refunds made later still count it as owed to the customer, on the memo side as well as the invoice side.
/// </summary>
public class CreditMemoFxRevaluationTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int EurId = 2;
    private static readonly DateOnly AsOf = new(2026, 3, 31);
    private static readonly DateTimeOffset March = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset April = new(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default) => Task.FromResult(_next++);
    }

    private static FxRevaluationService Service(AppDbContext db)
        => new(db, new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock()));

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" },
            new Currency { Id = EurId, Code = "EUR", Name = "Euro", Symbol = "€" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        var customer = new Customer { Id = 1, Name = "Acme" };
        db.Customers.Add(customer);
        db.Invoices.Add(new Invoice
        {
            Id = 1, InvoiceNumber = "INV-1", CustomerId = 1, CurrencyId = EurId, FxRate = 1.10m,
            Status = InvoiceStatus.PartiallyPaid, InvoiceDate = March, DueDate = March,
        });
        db.CreditMemos.AddRange(
            new CreditMemo { Id = 1, CreditMemoNumber = "CM-000001", CustomerId = 1, CurrencyId = EurId, FxRate = 1.10m, Reason = "Rebate", Status = CreditMemoStatus.Applied, CreditDate = March },
            new CreditMemo { Id = 2, CreditMemoNumber = "CM-000002", CustomerId = 1, CurrencyId = EurId, FxRate = 1.10m, Reason = "Goodwill", Status = CreditMemoStatus.Refunded, CreditDate = March });
        db.CreditMemoApplications.Add(new CreditMemoApplication { Id = 1, CreditMemoId = 1, InvoiceId = 1, Amount = 100m, AppliedAt = April });
        db.CustomerRefunds.Add(new CustomerRefund
        {
            Id = 1, RefundNumber = "RF-000001", CustomerId = 1, CurrencyId = EurId, FxRate = 1.10m, Amount = 50m,
            RefundDate = April, CreditMemoId = 2, Status = CustomerRefundStatus.Recorded,
        });
        db.ArOpenItems.AddRange(
            new ArOpenItem
            {
                BookId = BookId, CustomerId = 1, SourceType = "Invoice", SourceId = 1, DocumentNumber = "INV-1",
                DocumentDate = March, DueDate = March, CurrencyId = EurId, FxRate = 1.10m,
                OriginalTxnAmount = 300m, OriginalFunctionalAmount = 330m, AppliedTxnAmount = 100m, AppliedFunctionalAmount = 110m,
                Status = OpenItemStatus.PartiallyApplied,
            },
            new ArOpenItem
            {
                BookId = BookId, CustomerId = 1, SourceType = "CreditMemo", SourceId = 1, DocumentNumber = "CM-000001",
                DocumentDate = March, DueDate = March, CurrencyId = EurId, FxRate = 1.10m, Sign = -1,
                OriginalTxnAmount = 100m, OriginalFunctionalAmount = 110m, AppliedTxnAmount = 100m, AppliedFunctionalAmount = 110m,
                Status = OpenItemStatus.Closed,
            },
            new ArOpenItem
            {
                BookId = BookId, CustomerId = 1, SourceType = "CreditMemo", SourceId = 2, DocumentNumber = "CM-000002",
                DocumentDate = March, DueDate = March, CurrencyId = EurId, FxRate = 1.10m, Sign = -1,
                OriginalTxnAmount = 50m, OriginalFunctionalAmount = 55m, AppliedTxnAmount = 50m, AppliedFunctionalAmount = 55m,
                Status = OpenItemStatus.Closed,
            });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Credits_used_after_the_as_of_date_still_count_as_owed_to_the_customer()
    {
        using var db = await SeedAsync();

        var result = await Service(db).RevalueAsync(BookId, EurId, newRate: 1.20m, AsOf, postedByUserId: 7, preview: true);

        result.NetForeignPosition.Should().Be(150m);
        result.Adjustment.Should().Be(15m);
    }

    [Fact]
    public async Task Credits_used_on_or_before_the_as_of_date_no_longer_count()
    {
        using var db = await SeedAsync();

        var result = await Service(db).RevalueAsync(BookId, EurId, newRate: 1.20m, new DateOnly(2026, 4, 30), postedByUserId: 7, preview: true);

        result.NetForeignPosition.Should().Be(200m);
        result.Adjustment.Should().Be(20m);
    }
}
