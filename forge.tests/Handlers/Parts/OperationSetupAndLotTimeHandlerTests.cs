using FluentAssertions;
using Moq;

using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

public class OperationSetupAndLotTimeHandlerTests
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

    private static CreateOperationRequestModel CreateRequest(decimal? setupMinutes, decimal? runMinutesLot) => new(
        StepNumber: 1, Title: "Mill", Instructions: null, WorkCenterId: null,
        EstimatedMs: 7_500L, IsQcCheckpoint: false, QcCriteria: null,
        ReferencedOperationId: null, SetupMinutes: setupMinutes, RunMinutesLot: runMinutesLot);

    private static UpdateOperationRequestModel UpdateRequest(decimal? setupMinutes, decimal? runMinutesLot) => new(
        StepNumber: null, Title: null, Instructions: null, WorkCenterId: null,
        EstimatedMs: null, IsQcCheckpoint: null, QcCriteria: null,
        ReferencedOperationId: null, SetupMinutes: setupMinutes, RunMinutesLot: runMinutesLot);

    [Fact]
    public async Task Create_RoundTripsSetupAndLotMinutes()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var handler = new CreateOperationHandler(NewPartRepo(db), Mock.Of<IVendorRepository>());

        var result = await handler.Handle(
            new CreateOperationCommand(part.Id, CreateRequest(45.5m, 12m)),
            CancellationToken.None);

        result.SetupMinutes.Should().Be(45.5m);
        result.RunMinutesLot.Should().Be(12m);
        var stored = await db.Operations.FindAsync(result.Id);
        stored!.SetupMinutes.Should().Be(45.5m);
        stored.RunMinutesLot.Should().Be(12m);
    }

    [Fact]
    public async Task Create_DefaultsOmittedSetupAndLotMinutesToZero()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var handler = new CreateOperationHandler(NewPartRepo(db), Mock.Of<IVendorRepository>());

        var result = await handler.Handle(
            new CreateOperationCommand(part.Id, CreateRequest(null, null)),
            CancellationToken.None);

        result.SetupMinutes.Should().Be(0m);
        result.RunMinutesLot.Should().Be(0m);
    }

    [Fact]
    public async Task Update_ChangesSetupAndLotMinutes()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var op = new Operation { PartId = part.Id, StepNumber = 1, Title = "Op", SetupMinutes = 30m, RunMinutesLot = 5m };
        db.Add(op);
        await db.SaveChangesAsync();

        var handler = new UpdateOperationHandler(NewPartRepo(db), Mock.Of<IVendorRepository>());
        var result = await handler.Handle(
            new UpdateOperationCommand(part.Id, op.Id, UpdateRequest(0m, 7.25m)),
            CancellationToken.None);

        result.SetupMinutes.Should().Be(0m);
        result.RunMinutesLot.Should().Be(7.25m);
    }

    [Fact]
    public async Task Update_LeavesSetupAndLotMinutesAloneWhenOmitted()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var op = new Operation { PartId = part.Id, StepNumber = 1, Title = "Op", SetupMinutes = 30m, RunMinutesLot = 5m };
        db.Add(op);
        await db.SaveChangesAsync();

        var handler = new UpdateOperationHandler(NewPartRepo(db), Mock.Of<IVendorRepository>());
        var result = await handler.Handle(
            new UpdateOperationCommand(part.Id, op.Id, UpdateRequest(null, null)),
            CancellationToken.None);

        result.SetupMinutes.Should().Be(30m);
        result.RunMinutesLot.Should().Be(5m);
    }

    [Theory]
    [InlineData(-1.0, null, "Data.SetupMinutes")]
    [InlineData(null, -0.5, "Data.RunMinutesLot")]
    public void CreateValidator_RejectsNegativeMinutes(double? setup, double? lot, string property)
    {
        var result = new CreateOperationValidator().Validate(
            new CreateOperationCommand(1, CreateRequest((decimal?)setup, (decimal?)lot)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == property);
    }

    [Theory]
    [InlineData(-1.0, null, "Data.SetupMinutes")]
    [InlineData(null, -0.5, "Data.RunMinutesLot")]
    public void UpdateValidator_RejectsNegativeMinutes(double? setup, double? lot, string property)
    {
        var result = new UpdateOperationValidator().Validate(
            new UpdateOperationCommand(1, 1, UpdateRequest((decimal?)setup, (decimal?)lot)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == property);
    }

    [Fact]
    public void Validators_AcceptZeroMinutes()
    {
        new CreateOperationValidator().Validate(new CreateOperationCommand(1, CreateRequest(0m, 0m)))
            .IsValid.Should().BeTrue();
        new UpdateOperationValidator().Validate(new UpdateOperationCommand(1, 1, UpdateRequest(0m, 0m)))
            .IsValid.Should().BeTrue();
    }
}
