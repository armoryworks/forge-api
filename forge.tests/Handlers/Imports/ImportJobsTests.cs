using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Imports;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Imports;

public class ImportJobsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    internal static ImportJobsHandler Handler(AppDbContext db)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);
        var identifiers = new Mock<IBusinessIdentifierService>();
        identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        var barcodes = new Mock<IBarcodeService>();
        barcodes.Setup(b => b.CreateBarcodeAsync(It.IsAny<BarcodeEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Barcode());
        return new ImportJobsHandler(db, new JobRepository(db, clock.Object), barcodes.Object, identifiers.Object, clock.Object);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        var track = new TrackType { Name = "Production", Code = "production", IsDefault = true, IsActive = true };
        track.Stages.Add(new JobStage { Name = "Quote", Code = "quote", SortOrder = 1, IsActive = true });
        track.Stages.Add(new JobStage { Name = "In Production", Code = "in_production", SortOrder = 2, IsActive = true });
        track.Stages.Add(new JobStage { Name = "Shipped", Code = "shipped", SortOrder = 3, IsActive = true });
        db.TrackTypes.Add(track);
        db.Customers.Add(new Customer { Name = "Acme", CustomerNumber = "C-100" });
        db.Parts.Add(new Part { PartNumber = "BRK-1", Name = "Bracket" });
        db.Jobs.Add(new Job { JobNumber = "J-900", Title = "Existing", TrackType = track, CurrentStage = track.Stages.First() });
        await db.SaveChangesAsync();
    }

    private static JobImportRowModel Row(string? number, string? customer = "C-100", string? part = "BRK-1", string? stage = null,
        decimal? qty = 10m, string? title = null) =>
        new($"r-{number}", number, title, customer, part, qty, Now.AddDays(-30), Now.AddDays(-5), null, stage, null, null, "PO-77", null);

    [Fact]
    public async Task Dry_run_reports_valid_rows_and_writes_nothing()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        var result = await Handler(db).Handle(new ImportJobsCommand([Row("J-1001"), Row("J-1002", stage: "Shipped")], Commit: false), CancellationToken.None);

        result.Committed.Should().BeFalse();
        result.ValidCount.Should().Be(2);
        result.Rows.Should().OnlyContain(r => r.Status == "Valid");
        (await db.Jobs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_numbers_in_the_file_and_on_file_are_row_errors()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new ImportJobsCommand([Row("J-2000"), Row("j-2000"), Row("J-900")], Commit: false), CancellationToken.None);

        result.InvalidCount.Should().Be(3);
        result.Rows[0].Errors.Should().ContainSingle(e => e.Contains("more than once"));
        result.Rows[2].Errors.Should().ContainSingle(e => e.Contains("already in use"));
    }

    [Fact]
    public async Task Unknown_customer_part_and_stage_are_row_errors()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new ImportJobsCommand([Row("J-3000", customer: "Nobody Inc", part: "NOPE", stage: "Painting")], Commit: false),
            CancellationToken.None);

        result.Rows.Single().Errors.Should().Contain(e => e.Contains("Unknown customer 'Nobody Inc'"))
            .And.Contain(e => e.Contains("Unknown part 'NOPE'"))
            .And.Contain(e => e.Contains("Unknown stage 'Painting'"));
    }

    [Fact]
    public async Task Commit_with_an_invalid_row_writes_nothing()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new ImportJobsCommand([Row("J-4000"), Row("J-4001", customer: "Ghost")], Commit: true), CancellationToken.None);

        result.Committed.Should().BeFalse();
        result.CreatedCount.Should().Be(0);
        (await db.Jobs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Commit_creates_jobs_keeping_numbers_stage_and_customer_po()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new ImportJobsCommand([Row("J-5000", stage: "shipped"), Row("J-5001", stage: "In Production", title: "Run 2")], Commit: true),
            CancellationToken.None);

        result.Committed.Should().BeTrue();
        result.CreatedCount.Should().Be(2);
        var shipped = await db.Jobs.Include(j => j.CurrentStage).Include(j => j.JobParts).SingleAsync(j => j.JobNumber == "J-5000");
        shipped.CurrentStage.Code.Should().Be("shipped");
        shipped.CompletedDate.Should().Be(Now.AddDays(-5));
        shipped.Title.Should().Be("BRK-1");
        shipped.Description.Should().Contain("Customer PO: PO-77");
        shipped.CustomerId.Should().Be((await db.Customers.SingleAsync()).Id);
        shipped.JobParts.Should().ContainSingle().Which.Quantity.Should().Be(10m);
        var running = await db.Jobs.Include(j => j.CurrentStage).SingleAsync(j => j.JobNumber == "J-5001");
        running.CompletedDate.Should().BeNull();
        running.Title.Should().Be("Run 2");
        result.Rows.Select(r => r.CreatedId).Should().OnlyContain(id => id.HasValue);
    }

    [Fact]
    public async Task A_quantity_needs_a_part()
    {
        using var db = TestDbContextFactory.Create();
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new ImportJobsCommand([Row("J-6000", part: null, title: "Service call")], Commit: false), CancellationToken.None);

        result.Rows.Single().Errors.Should().Contain(e => e.Contains("needs a part"));
    }
}
