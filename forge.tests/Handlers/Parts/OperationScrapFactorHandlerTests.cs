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

public class OperationScrapFactorHandlerTests
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

    private static CreateOperationRequestModel CreateRequest(decimal? scrapFactor) => new(
        StepNumber: 1, Title: "Mold", Instructions: null, WorkCenterId: null,
        EstimatedMs: null, IsQcCheckpoint: false, QcCriteria: null,
        ReferencedOperationId: null, ScrapFactor: scrapFactor);

    private static UpdateOperationRequestModel UpdateRequest(decimal? scrapFactor) => new(
        StepNumber: null, Title: null, Instructions: null, WorkCenterId: null,
        EstimatedMs: null, IsQcCheckpoint: null, QcCriteria: null,
        ReferencedOperationId: null, ScrapFactor: scrapFactor);

    [Fact]
    public async Task Create_RoundTripsScrapFactor()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var handler = new CreateOperationHandler(NewPartRepo(db), Mock.Of<IVendorRepository>());

        var result = await handler.Handle(
            new CreateOperationCommand(part.Id, CreateRequest(0.025m)),
            CancellationToken.None);

        result.ScrapFactor.Should().Be(0.025m);
        var stored = await db.Operations.FindAsync(result.Id);
        stored!.ScrapFactor.Should().Be(0.025m);
    }

    [Fact]
    public async Task Create_DefaultsOmittedScrapFactorToZero()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var handler = new CreateOperationHandler(NewPartRepo(db), Mock.Of<IVendorRepository>());

        var result = await handler.Handle(
            new CreateOperationCommand(part.Id, CreateRequest(null)),
            CancellationToken.None);

        result.ScrapFactor.Should().Be(0m);
    }

    [Fact]
    public async Task Update_ChangesScrapFactor()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var op = new Operation { PartId = part.Id, StepNumber = 1, Title = "Op", ScrapFactor = 0.05m };
        db.Add(op);
        await db.SaveChangesAsync();

        var handler = new UpdateOperationHandler(NewPartRepo(db), Mock.Of<IVendorRepository>());
        var result = await handler.Handle(
            new UpdateOperationCommand(part.Id, op.Id, UpdateRequest(0m)),
            CancellationToken.None);

        result.ScrapFactor.Should().Be(0m);
    }

    [Fact]
    public async Task Update_LeavesScrapFactorAloneWhenOmitted()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var op = new Operation { PartId = part.Id, StepNumber = 1, Title = "Op", ScrapFactor = 0.05m };
        db.Add(op);
        await db.SaveChangesAsync();

        var handler = new UpdateOperationHandler(NewPartRepo(db), Mock.Of<IVendorRepository>());
        var result = await handler.Handle(
            new UpdateOperationCommand(part.Id, op.Id, UpdateRequest(null)),
            CancellationToken.None);

        result.ScrapFactor.Should().Be(0.05m);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Validators_RejectScrapFactorOutsideZeroToOne(double scrapFactor)
    {
        new CreateOperationValidator().Validate(new CreateOperationCommand(1, CreateRequest((decimal)scrapFactor)))
            .Errors.Should().ContainSingle(e => e.PropertyName == "Data.ScrapFactor");
        new UpdateOperationValidator().Validate(new UpdateOperationCommand(1, 1, UpdateRequest((decimal)scrapFactor)))
            .Errors.Should().ContainSingle(e => e.PropertyName == "Data.ScrapFactor");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.9999)]
    public void Validators_AcceptScrapFactorFromZeroToBelowOne(double scrapFactor)
    {
        new CreateOperationValidator().Validate(new CreateOperationCommand(1, CreateRequest((decimal)scrapFactor)))
            .IsValid.Should().BeTrue();
        new UpdateOperationValidator().Validate(new UpdateOperationCommand(1, 1, UpdateRequest((decimal)scrapFactor)))
            .IsValid.Should().BeTrue();
    }
}
