using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Tests.Reports;

/// <summary>
/// WU-15 / RPT-QUOTECONV-001 — contract test for /reports/quote-to-close.
///
/// Phase 1 found this endpoint returned [] despite 21 quotes because the
/// controller bound missing date params to DateTimeOffset.MinValue. Fix:
/// controller now defaults to a trailing 12-month window. This test guards
/// the contract: seeded quotes in-window must surface in the by-status
/// aggregation.
/// </summary>
public class QuoteToCloseContractTests : ReportContractTestBase
{
    protected override string EndpointName => "/reports/quote-to-close";

    protected override async Task SeedAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Contract Test Co", IsActive = true };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        // Two quotes — at least one accepted/converted so the report yields
        // multiple status buckets to aggregate.
        var draft = new Quote
        {
            QuoteNumber = "QT-CT-0001",
            CustomerId = customer.Id,
            Status = QuoteStatus.Draft,
            Type = QuoteType.Quote,
        };
        var converted = new Quote
        {
            QuoteNumber = "QT-CT-0002",
            CustomerId = customer.Id,
            Status = QuoteStatus.ConvertedToOrder,
            Type = QuoteType.Quote,
            ConvertedAt = DateTimeOffset.UtcNow.AddDays(-3),
        };
        db.Quotes.AddRange(draft, converted);
        await db.SaveChangesAsync();

        db.QuoteLines.AddRange(
            new QuoteLine
            {
                QuoteId = draft.Id,
                LineNumber = 1,
                Description = "Test line A",
                Quantity = 1,
                UnitPrice = 1000m,
            },
            new QuoteLine
            {
                QuoteId = converted.Id,
                LineNumber = 1,
                Description = "Test line B",
                Quantity = 2,
                UnitPrice = 1500m,
            });
        await db.SaveChangesAsync();
    }

    protected override async Task<object?> InvokeAsync(AppDbContext db)
    {
        var repo = RepoFor(db);
        var end = DateTimeOffset.UtcNow;
        var start = end.AddMonths(-12);
        return await repo.GetQuoteToCloseAsync(start, end, CancellationToken.None);
    }
}
