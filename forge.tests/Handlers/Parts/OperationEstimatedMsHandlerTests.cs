using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

/// <summary>
/// Round-trips <see cref="Operation.EstimatedMs"/> (canonical milliseconds) through the
/// create/update operation handlers, and checks the Hours/Minutes/Milliseconds compose math the
/// routing editor uses (<c>hours*3600000 + minutes*60000 + ms</c>).
/// </summary>
public class OperationEstimatedMsHandlerTests
{
    private static PartRepository NewPartRepo(Forge.Data.Context.AppDbContext db)
        => new(db, Mock.Of<IPartPricingResolver>());

    private static async Task<Part> SeedPartAsync(Forge.Data.Context.AppDbContext db)
    {
        var part = new Part
        {
            PartNumber = $"P-{Guid.NewGuid():N}",
            Name = "Test",
            ProcurementSource = ProcurementSource.Make,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
        };
        db.Add(part);
        await db.SaveChangesAsync();
        return part;
    }

    [Theory]
    [InlineData(0, 0, 0, 0L)]
    [InlineData(0, 0, 500, 500L)]           // sub-second only
    [InlineData(0, 1, 30_000, 90_000L)]     // 1 min 30 s
    [InlineData(1, 0, 0, 3_600_000L)]       // 1 hr
    [InlineData(2, 15, 250, 8_100_250L)]    // 2 hr 15 min 0.25 s
    public void Compose_HoursMinutesMs_ProducesCanonicalMs(int hours, int minutes, int ms, long expected)
    {
        var composed = (hours * 3_600_000L) + (minutes * 60_000L) + ms;
        composed.Should().Be(expected);
    }

    [Fact]
    public async Task Create_RoundTripsEstimatedMs()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var handler = new CreateOperationHandler(NewPartRepo(db), Mock.Of<IVendorRepository>());

        // 1 hr 30 min 500 ms → 5,400,500 ms.
        var result = await handler.Handle(
            new CreateOperationCommand(part.Id, new CreateOperationRequestModel(
                StepNumber: 1, Title: "Mill", Instructions: null, WorkCenterId: null,
                EstimatedMs: 5_400_500L, IsQcCheckpoint: false, QcCriteria: null,
                ReferencedOperationId: null)),
            CancellationToken.None);

        result.EstimatedMs.Should().Be(5_400_500L);
        var stored = await db.Operations.FindAsync(result.Id);
        stored!.EstimatedMs.Should().Be(5_400_500L);
    }

    [Fact]
    public async Task Create_AllowsNullEstimatedMs()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var handler = new CreateOperationHandler(NewPartRepo(db), Mock.Of<IVendorRepository>());

        var result = await handler.Handle(
            new CreateOperationCommand(part.Id, new CreateOperationRequestModel(
                StepNumber: 1, Title: "Deburr", Instructions: null, WorkCenterId: null,
                EstimatedMs: null, IsQcCheckpoint: false, QcCriteria: null,
                ReferencedOperationId: null)),
            CancellationToken.None);

        result.EstimatedMs.Should().BeNull();
    }

    [Fact]
    public async Task Update_ChangesEstimatedMs()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        db.Add(new Operation { PartId = part.Id, StepNumber = 1, Title = "Op", EstimatedMs = 60_000L });
        await db.SaveChangesAsync();
        var op = await db.Operations.FirstAsync();

        var handler = new UpdateOperationHandler(NewPartRepo(db), Mock.Of<IVendorRepository>());
        var result = await handler.Handle(
            new UpdateOperationCommand(part.Id, op.Id, new UpdateOperationRequestModel(
                StepNumber: null, Title: null, Instructions: null, WorkCenterId: null,
                EstimatedMs: 250L, IsQcCheckpoint: null, QcCriteria: null,
                ReferencedOperationId: null)),
            CancellationToken.None);

        result.EstimatedMs.Should().Be(250L);
    }
}
