using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

using static Forge.Tests.Helpers.InventoryLedgerTestBook;

namespace Forge.Tests.Accounting;

public class InventoryValuationReconciliationTests
{
    private static readonly DateOnly EntryDate = new(2026, 2, 10);

    private static Task PostAsync(AppDbContext db, string sourceType, JournalSource source, string key, params PostingLine[] lines)
        => Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = EntryDate, Source = source, SourceType = sourceType, CurrencyId = 1,
            IdempotencyKey = key, Lines = [.. lines],
        }, 7);

    /// <summary>A raw receipt (50) partly issued to a job (30), the job's output received at standard (100), and
    /// an opening journal (1,000) posted straight to raw inventory.</summary>
    internal static async Task<int> SeedScenarioAsync(AppDbContext db, bool withOpeningJournal)
    {
        await InventoryLedgerTestBook.SeedAsync(db);
        var track = new TrackType { Name = "Recon track", Code = "recon-track", IsActive = true };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Stage 1", Code = "s1", SortOrder = 1, IsActive = true };
        db.JobStages.Add(stage);
        await db.SaveChangesAsync();
        var job = new Job { JobNumber = "J-RECON", Title = "Bracket run", TrackTypeId = track.Id, CurrentStageId = stage.Id };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        var jobId = job.Id;
        var raw = new Part { PartNumber = "RAW-1", Description = "Bar", InventoryClass = InventoryClass.Raw };
        var fg = new Part { PartNumber = "FG-1", Description = "Bracket", InventoryClass = InventoryClass.FinishedGood };
        db.Parts.AddRange(raw, fg);
        await db.SaveChangesAsync();

        await PostAsync(db, "Receipt", JournalSource.Inventory, "r1",
            new PostingLine { AccountKey = "INVENTORY_RAW", Debit = 50m },
            new PostingLine { AccountKey = "INVENTORY_WRITEDOWN", Credit = 50m });
        await PostAsync(db, "MaterialIssue", JournalSource.Inventory, "m1",
            new PostingLine { AccountKey = "INVENTORY_WIP", Debit = 30m, JobId = jobId },
            new PostingLine { AccountKey = "INVENTORY_RAW", Credit = 30m });
        await PostAsync(db, "ProductionRun", JournalSource.Inventory, "p1",
            new PostingLine { AccountKey = "INVENTORY_FG", Debit = 100m },
            new PostingLine { AccountKey = "INVENTORY_WIP", Credit = 100m, JobId = jobId });
        if (withOpeningJournal)
            await PostAsync(db, "OpeningJournal", JournalSource.Conversion, "o1",
                new PostingLine { AccountKey = "INVENTORY_RAW", Debit = 1000m },
                new PostingLine { AccountKey = "INVENTORY_WRITEDOWN", Credit = 1000m });

        db.Set<InventoryValuation>().AddRange(
            new InventoryValuation { BookId = BookId, PartId = raw.Id, OnHandQuantity = 4m, AverageUnitCost = 5m, TotalValue = 20m },
            new InventoryValuation { BookId = BookId, PartId = fg.Id, OnHandQuantity = 1m, AverageUnitCost = 100m, TotalValue = 100m });
        await db.SaveChangesAsync();
        return jobId;
    }

    internal static void AssertItemised(InventoryValuationReconciliation recon, int jobId)
    {
        recon.GlInventoryBalance.Should().Be(1050m);
        recon.GlWorkInProcessBalance.Should().Be(-70m);
        recon.StoreValue.Should().Be(120m);
        recon.Variance.Should().Be(-1000m);
        recon.IsReconciled.Should().BeFalse();
        recon.ReconcilingItems.Should().ContainSingle();
        var item = recon.ReconcilingItems[0];
        (item.Kind, item.AccountNumber, item.SourceType, item.Amount).Should().Be(("PostedOutsideStore", "13100", "OpeningJournal", -1000m));
        recon.ReconcilingItems.Sum(i => i.Amount).Should().Be(recon.Variance);
        recon.WorkInProcessByJob.Should().ContainSingle().Which.Should().Be(new InventoryWorkInProcessByJob(jobId, -70m));
        recon.Accounts.Select(a => (a.AccountNumber, a.StoreValue, a.GlBalance))
            .Should().Equal(("13100", 20m, 1020m), ("13250", 0m, 0m), ("13300", 100m, 100m));
    }

    [Fact]
    public async Task OpenJobWip_IsReportedApart_AndDoesNotBreakTheTie()
    {
        using var db = TestDbContextFactory.Create();
        await SeedScenarioAsync(db, withOpeningJournal: false);

        var recon = await new InventoryValuationService(db).ReconcileAsync(BookId);

        recon.GlWorkInProcessBalance.Should().Be(-70m);
        recon.Variance.Should().Be(0m);
        recon.IsReconciled.Should().BeTrue();
        recon.ReconcilingItems.Should().BeEmpty();
    }

    [Fact]
    public async Task OpeningJournal_IsItemised_AndItemsSumToTheVariance()
    {
        using var db = TestDbContextFactory.Create();
        var jobId = await SeedScenarioAsync(db, withOpeningJournal: true);

        AssertItemised(await new InventoryValuationService(db).ReconcileAsync(BookId), jobId);
    }

    [Fact]
    public async Task StoreDrift_OnAStoreFedSource_IsUnexplained()
    {
        using var db = TestDbContextFactory.Create();
        await SeedScenarioAsync(db, withOpeningJournal: false);
        await PostAsync(db, "Invoice", JournalSource.AR, "i1",
            new PostingLine { AccountKey = "INVENTORY_WRITEDOWN", Debit = 12.5m },
            new PostingLine { AccountKey = "INVENTORY_FG", Credit = 12.5m });

        var recon = await new InventoryValuationService(db).ReconcileAsync(BookId);

        recon.Variance.Should().Be(12.5m);
        var item = recon.ReconcilingItems.Should().ContainSingle().Subject;
        (item.Kind, item.AccountNumber, item.Amount).Should().Be(("Unexplained", "13300", 12.5m));
    }
}
