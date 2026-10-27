using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Scanner;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Regression for a job carrying work in process with no job cost (graded run S18-27): a scanner issue of a part
/// with no purchase history, valued from the perpetual store, debited WIP at the store's average cost while the issue
/// kept a zero unit cost, so the job cost summary showed nothing. The issue now stores the relieved cost, and the
/// job's material cost equals the WIP its issue journals debited.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ScanIssueJobCostPostgresTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
    }

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
    public async Task A_scanner_issue_valued_from_the_store_records_its_cost_on_the_issue_and_the_job()
    {
        int jobId, partId, userId, locationId;
        await using (var seed = fixture.CreateContext())
        {
            await ResetAsync(seed);
            await InventoryLedgerTestBook.SeedAsync(seed);
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var user = new ApplicationUser
            {
                UserName = $"si{suffix}@forge.local", Email = $"si{suffix}@forge.local", FirstName = "Scan", LastName = "Issue", IsActive = true,
            };
            var track = new TrackType { Name = $"Scan {suffix}", Code = $"si-{suffix}", IsActive = true };
            var resin = new Part { PartNumber = $"ALT-{suffix}", Name = "Alternate resin", InventoryClass = InventoryClass.Raw };
            var location = new StorageLocation { Name = $"RC-{suffix}", LocationType = LocationType.Bin, IsActive = true };
            seed.AddRange(user, track, resin, location);
            await seed.SaveChangesAsync();
            var stage = new JobStage { TrackTypeId = track.Id, Name = "Mold", Code = "mold", SortOrder = 1, IsActive = true };
            seed.JobStages.Add(stage);
            await seed.SaveChangesAsync();
            var job = new Job { JobNumber = $"J-{suffix}", Title = "Clips", TrackTypeId = track.Id, CurrentStageId = stage.Id };
            seed.Jobs.Add(job);
            seed.BinContents.Add(new BinContent
            {
                LocationId = location.Id, EntityType = "part", EntityId = resin.Id, Quantity = 110m, PlacedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            seed.InventoryValuations.Add(new InventoryValuation
            {
                BookId = InventoryLedgerTestBook.BookId, PartId = resin.Id, OnHandQuantity = 110m, AverageUnitCost = 3.4m, TotalValue = 374m,
            });
            await seed.SaveChangesAsync();
            (jobId, partId, userId, locationId) = (job.Id, resin.Id, user.Id, location.Id);
        }

        await using (var db = fixture.CreateContext())
        {
            var posting = new MaterialIssuePostingService(
                db, InventoryLedgerTestBook.Engine(db), new StubCapabilitySnapshotProvider(InventoryLedgerTestBook.FullGl),
                valuation: new InventoryValuationService(db));
            var handler = new ExecuteScanIssueHandler(
                db, new SystemClock(), InventoryLedgerTestBook.UserAccessor(userId), MaterialIssueMediator.For(db, posting));

            await handler.Handle(new ExecuteScanIssueCommand(new ScanIssueRequestModel(partId, jobId, 10m, locationId)), CancellationToken.None);
        }

        await using var read = fixture.CreateContext();
        var issue = await read.MaterialIssues.AsNoTracking().SingleAsync(m => m.JobId == jobId);
        issue.UnitCost.Should().Be(3.4m);
        var wip = await read.JournalLines.AsNoTracking()
            .Where(l => l.GlAccountId == InventoryLedgerTestBook.WipId && l.JobId == jobId)
            .SumAsync(l => l.Debit - l.Credit);
        wip.Should().Be(34m);
        var summary = await new JobCostService(read).GetCostSummaryAsync(jobId, CancellationToken.None);
        summary.MaterialActual.Should().Be(wip);
        summary.TotalActual.Should().Be(wip);
    }
}
