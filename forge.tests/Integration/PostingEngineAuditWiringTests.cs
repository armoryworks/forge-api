using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Integration;

[Collection(CapabilityTestCollection.Name)]
public class PostingEngineAuditWiringTests(CapabilityTestWebApplicationFactory factory)
{
    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(Interlocked.Increment(ref _next));
    }

    [Fact]
    public async Task EngineResolvedFromTheAppContainer_WritesThePostingAuditRow()
    {
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IAcctNumberSequenceAllocator>(new FakeAllocator()))));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tag = Guid.NewGuid().ToString("N")[..8];

        var currency = await db.Set<Currency>().FirstOrDefaultAsync(c => c.Code == "USD");
        if (currency is null)
        {
            currency = new Currency { Code = "USD", Name = "US Dollar", Symbol = "$" };
            db.Set<Currency>().Add(currency);
            await db.SaveChangesAsync();
        }

        var book = new Book
        {
            Code = $"AW{tag}", Name = $"Audit wiring {tag}", FunctionalCurrencyId = currency.Id,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        };
        db.Set<Book>().Add(book);
        await db.SaveChangesAsync();

        var year = new FiscalYear
        {
            BookId = book.Id, Name = $"FY {tag}",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        };
        db.Set<FiscalYear>().Add(year);
        await db.SaveChangesAsync();

        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            FiscalYearId = year.Id, PeriodNumber = 1, Name = $"Jan {tag}",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            Status = FiscalPeriodStatus.Open,
        });
        var cash = new GlAccount
        {
            BookId = book.Id, AccountNumber = "1000", Name = "Cash",
            AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true,
        };
        var expense = new GlAccount
        {
            BookId = book.Id, AccountNumber = "6000", Name = "Expense",
            AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true,
        };
        db.Set<GlAccount>().AddRange(cash, expense);
        await db.SaveChangesAsync();

        var engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        var entry = await engine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = new DateOnly(2026, 1, 15),
            Source = JournalSource.AP,
            IdempotencyKey = $"ap:vendor_bill:{tag}:approve",
            CurrencyId = currency.Id,
            Memo = "Audit wiring JE",
            Lines =
            [
                new PostingLine { GlAccountId = expense.Id, Debit = 25m },
                new PostingLine { GlAccountId = cash.Id, Credit = 25m },
            ],
        }, actingUserId: 42);

        var marker = $"\"journalEntryId\":{entry.Id},";
        var rows = await db.AuditLogEntries.AsNoTracking()
            .Where(a => a.EntityType == nameof(JournalEntry) && a.Details != null && a.Details.Contains(marker))
            .ToListAsync();

        rows.Should().ContainSingle();
        rows[0].Action.Should().Be("GlJournalPosted");
        rows[0].UserId.Should().Be(42);
    }
}
