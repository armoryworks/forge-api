using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Accounting.StockLoads;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting.StockLoads;

/// <summary>
/// The stock-load tables, queries, advisory locks and the post / void transaction against real Postgres, where the
/// InMemory provider cannot show whether the schema matches the mapping or the queries translate.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StockLoadPostgresTests(PostgresFixture fixture)
{
    private sealed record Seeded(int BookId, int RawAccountId, int FgAccountId, int ObeAccountId, int UserId);

    private static IHttpContextAccessor UserAccessor(int userId) => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
        },
    };

    private static StockLoadService Service(AppDbContext db) => new(
        db,
        new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new AcctNumberSequenceAllocator(db), new SystemClock()),
        new InventoryValuationService(db),
        new InventoryRepository(db),
        new StubCapabilitySnapshotProvider(),
        new SystemClock());

    private async Task<(Seeded Seeded, string Suffix)> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var key = Random.Shared.Next(100_000, 900_000);
        var user = new ApplicationUser
        {
            UserName = $"sl{suffix}@forge.local", Email = $"sl{suffix}@forge.local", FirstName = "Stock", LastName = "Load", IsActive = true,
        };
        var currency = new Currency { Id = key, Code = $"S{suffix}", Name = "Stock load test", Symbol = "$" };
        db.AddRange(user, currency);
        await db.SaveChangesAsync();

        var book = new Book
        {
            Id = key, Code = $"SL{suffix}", Name = "Stock load book", FunctionalCurrencyId = currency.Id,
            ReportingTimeZone = "UTC", RoundingTolerance = 0.01m, IsActive = true,
        };
        db.Add(book);
        await db.SaveChangesAsync();
        var year = new FiscalYear
        {
            Id = key, BookId = book.Id, Name = $"FY{suffix}", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        };
        db.Add(year);
        await db.SaveChangesAsync();
        db.Add(new FiscalPeriod
        {
            Id = key, FiscalYearId = year.Id, PeriodNumber = 1, Name = "FY2026", StartDate = year.StartDate, EndDate = year.EndDate,
            Status = FiscalPeriodStatus.Open,
        });
        GlAccount Inventory(int id, string number, string name) => new()
        {
            Id = id, BookId = book.Id, AccountNumber = number, Name = name, AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit,
            IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true,
        };
        var raw = Inventory(key * 10 + 1, "13100", "Inventory — Raw Materials");
        var fg = Inventory(key * 10 + 2, "13300", "Inventory — Finished Goods");
        var obe = new GlAccount
        {
            Id = key * 10 + 3, BookId = book.Id, AccountNumber = "39000", Name = "Opening Balance Equity", AccountType = AccountType.Equity,
            NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true,
        };
        db.AddRange(raw, fg, obe);
        await db.SaveChangesAsync();
        db.AddRange(
            new AccountDeterminationRule { Id = key * 10 + 1, BookId = book.Id, Key = "INVENTORY_RAW", GlAccountId = raw.Id },
            new AccountDeterminationRule { Id = key * 10 + 2, BookId = book.Id, Key = "INVENTORY_FG", GlAccountId = fg.Id },
            new AccountDeterminationRule { Id = key * 10 + 3, BookId = book.Id, Key = "OPENING_BALANCE_EQUITY", GlAccountId = obe.Id });
        db.AddRange(
            new Part { PartNumber = $"FG-{suffix}", Name = "Housing", InventoryClass = InventoryClass.FinishedGood },
            new Part { PartNumber = $"RM-{suffix}", Name = "Resin", InventoryClass = InventoryClass.Raw },
            new StorageLocation { Name = $"SL-A-{suffix}", Barcode = $"SLA{suffix}", LocationType = LocationType.Bin, IsActive = true });
        await db.SaveChangesAsync();
        return (new Seeded(book.Id, raw.Id, fg.Id, obe.Id, user.Id), suffix);
    }

    [Fact]
    public async Task Create_post_reconcile_and_void_round_trip()
    {
        var (s, suffix) = await SeedAsync();
        int loadId;

        await using (var db = fixture.CreateContext())
        {
            var created = await new CreateStockLoadHandler(db, Service(db), UserAccessor(s.UserId)).Handle(
                new CreateStockLoadCommand(s.BookId, new DateOnly(2026, 1, 1), StockLoadGlMode.OffsetEquity, "Cutover", null,
                [
                    new StockLoadLineInput($"FG-{suffix}", $"SLA{suffix}", null, null, 200m, 471m),
                    new StockLoadLineInput($"rm-{suffix}", $"SL-A-{suffix}", "L1", new DateOnly(2027, 1, 1), 1000m, 94.2m),
                ]),
                CancellationToken.None);
            created.LoadNumber.Should().StartWith("SL-");
            loadId = created.Id;
            (await new GetStockLoadsHandler(db).Handle(new GetStockLoadsQuery(s.BookId), CancellationToken.None))
                .Single().TotalValue.Should().Be(188_400m);
        }

        await using (var db = fixture.CreateContext())
        {
            var posted = await new PostStockLoadHandler(Service(db), UserAccessor(s.UserId))
                .Handle(new PostStockLoadCommand(loadId), CancellationToken.None);
            posted.Status.Should().Be(StockLoadStatus.Posted);
            posted.JournalEntryNumber.Should().NotBeNull();
        }

        await using (var db = fixture.CreateContext())
        {
            var lines = await db.JournalLines.Where(l => db.JournalEntries
                    .Any(e => e.Id == l.JournalEntryId && e.IdempotencyKey == $"Conversion:StockLoad:{loadId}"))
                .ToListAsync();
            lines.Single(l => l.GlAccountId == s.FgAccountId).Debit.Should().Be(94_200m);
            lines.Single(l => l.GlAccountId == s.RawAccountId).Debit.Should().Be(94_200m);
            lines.Single(l => l.GlAccountId == s.ObeAccountId).Credit.Should().Be(188_400m);

            var rec = await new InventoryValuationService(db).ReconcileAsync(s.BookId);
            rec.Variance.Should().Be(0m);
            rec.ReconcilingItems.Should().BeEmpty();
            var context = await Service(db).GetContextAsync(s.BookId);
            context.OpeningJournalInventory.Should().BeEmpty();
        }

        await using (var db = fixture.CreateContext())
        {
            var voided = await new VoidStockLoadHandler(Service(db), UserAccessor(s.UserId))
                .Handle(new VoidStockLoadCommand(loadId), CancellationToken.None);
            voided.Status.Should().Be(StockLoadStatus.Voided);
        }

        await using (var db = fixture.CreateContext())
        {
            var rec = await new InventoryValuationService(db).ReconcileAsync(s.BookId);
            rec.StoreValue.Should().Be(0m);
            rec.Variance.Should().Be(0m);
            rec.ReconcilingItems.Should().BeEmpty();
        }
    }
}
