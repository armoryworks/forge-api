using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// The inventory valuation listing must translate to SQL on real Postgres: sorting by part number after
/// projecting into <see cref="Forge.Core.Models.Accounting.InventoryValuationModel"/> is untranslatable and
/// returned a 500. The InMemory provider evaluates such queries client-side, so only Postgres proves the fix.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InventoryValuationQueryPostgresTests(PostgresFixture fixture)
{
    private const int BookId = 1;
    private const int UsdId = 1;

    private static Task ResetAsync(AppDbContext db)
        => db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT tablename FROM pg_tables
            WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory') LOOP
    EXECUTE 'TRUNCATE TABLE ' || quote_ident(r.tablename) || ' RESTART IDENTITY CASCADE';
  END LOOP;
END $$;");

    [Fact]
    public async Task GetAsync_ListsOnHandPartsOrderedByPartNumber()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        var zeta = new Part { PartNumber = "ZZ-200", Description = "Zeta" };
        var alpha = new Part { PartNumber = "AA-100", Description = "Alpha" };
        var empty = new Part { PartNumber = "MM-150", Description = "Issued out" };
        db.Parts.AddRange(zeta, alpha, empty);
        await db.SaveChangesAsync();
        db.Set<InventoryValuation>().AddRange(
            new InventoryValuation { BookId = BookId, PartId = zeta.Id, OnHandQuantity = 2m, AverageUnitCost = 5m, TotalValue = 10m },
            new InventoryValuation { BookId = BookId, PartId = alpha.Id, OnHandQuantity = 4m, AverageUnitCost = 2.5m, TotalValue = 10m },
            new InventoryValuation { BookId = BookId, PartId = empty.Id, OnHandQuantity = 0m, AverageUnitCost = 3m, TotalValue = 0m });
        await db.SaveChangesAsync();

        var rows = await new InventoryValuationService(db).GetAsync(BookId);

        rows.Select(r => (r.PartNumber, r.OnHandQuantity, r.TotalValue))
            .Should().Equal(("AA-100", 4m, 10m), ("ZZ-200", 2m, 10m));
    }
}
