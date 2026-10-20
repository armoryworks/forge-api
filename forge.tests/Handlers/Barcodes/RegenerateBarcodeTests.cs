using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Barcodes;
using Forge.Api.Middleware;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Barcodes;

public class RegenerateBarcodeTests
{
    private static async Task<(AppDbContext Db, Part Part)> SeedPartAsync()
    {
        var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-REGEN", Name = "Bracket", Description = "Bracket" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        await new BarcodeService(db, new HttpContextAccessor()).CreateBarcodeAsync(BarcodeEntityType.Part, part.Id, part.PartNumber);
        return (db, part);
    }

    private static RegenerateBarcodeHandler Handler(AppDbContext db, IHttpContextAccessor? caller = null) =>
        new(db, new BarcodeService(db, caller ?? new HttpContextAccessor()), caller);

    [Fact]
    public async Task Regenerate_twice_leaves_one_active_barcode_with_a_value_never_used_before()
    {
        var (db, part) = await SeedPartAsync();
        await using var _ = db;

        var first = await Handler(db).Handle(new RegenerateBarcodeCommand(BarcodeEntityType.Part, part.Id, part.PartNumber), default);
        var second = await Handler(db).Handle(new RegenerateBarcodeCommand(BarcodeEntityType.Part, part.Id, part.PartNumber), default);

        var all = await db.Barcodes.IgnoreQueryFilters().Where(b => b.PartId == part.Id).ToListAsync();
        all.Select(b => b.Value).Should().OnlyHaveUniqueItems();
        all.Should().HaveCount(3);
        all.Where(b => b.DeletedAt == null).Should().ContainSingle(b => b.Value == second.Value && b.IsActive);
        first.Value.Should().Be($"PRT-P-REGEN-{part.Id}");
        second.Value.Should().Be($"PRT-P-REGEN-{part.Id}-2");
    }

    [Fact]
    public async Task Regenerate_a_job_barcode_needs_an_admin_or_manager()
    {
        await using var db = TestDbContextFactory.Create();
        var job = new Job { JobNumber = "J-REGEN", Title = "Regen" };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        var command = new RegenerateBarcodeCommand(BarcodeEntityType.Job, job.Id, job.JobNumber);

        var asOperator = () => Handler(db, TestCaller.As(5, "ProductionWorker")).Handle(command, default);
        await asOperator.Should().ThrowAsync<ForbiddenException>();
        (await db.Barcodes.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        var result = await Handler(db, TestCaller.As(6, "Manager")).Handle(command, default);
        result.Value.Should().Be("JOB-J-REGEN");
    }

    [Fact]
    public async Task Regenerate_a_gtin_part_reuses_its_retired_gtin_barcode()
    {
        await using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-GTIN", Name = "Widget", Description = "Widget", Gtin = "00012345678905" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        await new BarcodeService(db, new HttpContextAccessor()).CreateBarcodeAsync(BarcodeEntityType.Part, part.Id, part.PartNumber);

        await Handler(db).Handle(new RegenerateBarcodeCommand(BarcodeEntityType.Part, part.Id, part.PartNumber), default);
        var second = await Handler(db).Handle(new RegenerateBarcodeCommand(BarcodeEntityType.Part, part.Id, part.PartNumber), default);

        var all = await db.Barcodes.IgnoreQueryFilters().Where(b => b.PartId == part.Id).ToListAsync();
        all.Should().ContainSingle();
        all[0].Should().Match<Barcode>(b => b.Value == part.Gtin && b.IsActive && b.DeletedAt == null);
        second.Value.Should().Be(part.Gtin);
    }
}
