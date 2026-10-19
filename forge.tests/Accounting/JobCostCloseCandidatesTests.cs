using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

public class JobCostCloseCandidatesTests
{
    private const int WipAccountId = 5;
    private long _nextEntryId = 100;

    private static AppDbContext SeededDb()
    {
        var db = TestDbContextFactory.Create();
        db.Books.Add(new Book { Id = 1, Code = "MAIN", Name = "Main", FunctionalCurrencyId = 1, IsActive = true });
        db.GlAccounts.Add(new GlAccount { Id = WipAccountId, BookId = 1, AccountNumber = "13000", Name = "WIP", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { Id = 1, BookId = 1, Key = "INVENTORY_WIP", GlAccountId = WipAccountId });
        db.Jobs.AddRange(
            new Job { Id = 11, JobNumber = "J-0011", Title = "Open WIP, completed", CompletedDate = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero) },
            new Job { Id = 12, JobNumber = "J-0012", Title = "Open WIP, running" },
            new Job { Id = 13, JobNumber = "J-0013", Title = "Already closed" },
            new Job { Id = 14, JobNumber = "J-0014", Title = "Zero WIP" });
        db.SaveChanges();
        return db;
    }

    private void Post(AppDbContext db, int jobId, decimal debit, decimal credit, string? key = null)
    {
        var entryId = _nextEntryId++;
        db.JournalEntries.Add(new JournalEntry
        {
            Id = entryId, BookId = 1, EntryNumber = entryId, EntryDate = new DateOnly(2026, 9, 1),
            FiscalPeriodId = 1, FiscalYearId = 1, Source = JournalSource.Inventory, SourceType = "Job",
            Status = JournalEntryStatus.Posted, CurrencyId = 1, IdempotencyKey = key,
            Lines =
            [
                new JournalLine { BookId = 1, LineNumber = 1, GlAccountId = WipAccountId, JobId = jobId, Debit = debit, Credit = credit, CurrencyId = 1 },
            ],
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task ListsJobsWithAnOpenWipBalance_completedFirst_andSkipsClosedOrZeroJobs()
    {
        await using var db = SeededDb();
        Post(db, 12, 80m, 0m);
        Post(db, 11, 250m, 0m);
        Post(db, 11, 0m, 200m);
        Post(db, 13, 90m, 0m);
        Post(db, 13, 0m, 90m, "Inventory:Job:13:PRODVARIANCE");
        Post(db, 14, 40m, 0m);
        Post(db, 14, 0m, 40m);

        var result = await new GetJobCostCloseCandidatesHandler(db).Handle(new GetJobCostCloseCandidatesQuery(), default);

        result.Select(r => r.JobNumber).Should().Equal("J-0011", "J-0012");
        result[0].WipBalance.Should().Be(50m);
        result[1].WipBalance.Should().Be(80m);
        result.Select(r => r.IsComplete).Should().Equal(true, false);
    }

    [Fact]
    public async Task ReturnsNothing_withoutAWipAccountRule()
    {
        await using var db = TestDbContextFactory.Create();
        db.Books.Add(new Book { Id = 1, Code = "MAIN", Name = "Main", FunctionalCurrencyId = 1, IsActive = true });
        db.SaveChanges();

        var result = await new GetJobCostCloseCandidatesHandler(db).Handle(new GetJobCostCloseCandidatesQuery(), default);

        result.Should().BeEmpty();
    }
}
