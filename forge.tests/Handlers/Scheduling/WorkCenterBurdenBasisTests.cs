using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Parts;
using Forge.Api.Features.Scheduling;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Scheduling;

public class WorkCenterBurdenBasisTests
{
    [Theory]
    [InlineData(AssetType.Machine, BurdenBasis.Machine)]
    [InlineData(AssetType.Tooling, BurdenBasis.Labor)]
    public async Task A_new_work_center_defaults_its_burden_basis_from_its_asset(AssetType assetType, BurdenBasis expected)
    {
        using var db = TestDbContextFactory.Create();
        var asset = new Asset { Name = "Lathe 1", AssetType = assetType };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var withAsset = await new CreateWorkCenterHandler(db).Handle(new CreateWorkCenterCommand(
            "Lathe", "WC-LATHE", null, 8m, 100m, 1, 40m, 20m, asset.Id, null, 1), CancellationToken.None);
        var withoutAsset = await new CreateWorkCenterHandler(db).Handle(new CreateWorkCenterCommand(
            "Bench", "WC-BENCH", null, 8m, 100m, 1, 40m, 20m, null, null, 2), CancellationToken.None);

        withAsset.BurdenBasis.Should().Be(expected);
        withoutAsset.BurdenBasis.Should().Be(BurdenBasis.Labor);
    }

    [Fact]
    public async Task The_burden_basis_can_be_chosen_and_changed()
    {
        using var db = TestDbContextFactory.Create();
        var created = await new CreateWorkCenterHandler(db).Handle(new CreateWorkCenterCommand(
            "Mill", "WC-MILL", null, 8m, 100m, 1, 40m, 20m, null, null, 1, BurdenBasis: BurdenBasis.Machine), CancellationToken.None);
        created.BurdenBasis.Should().Be(BurdenBasis.Machine);

        var updated = await new UpdateWorkCenterHandler(db).Handle(new UpdateWorkCenterCommand(
            created.Id, "Mill", "WC-MILL", null, 8m, 100m, 1, 40m, 20m, true, null, null, 1, BurdenBasis: BurdenBasis.Labor), CancellationToken.None);
        updated.BurdenBasis.Should().Be(BurdenBasis.Labor);

        var unchanged = await new UpdateWorkCenterHandler(db).Handle(new UpdateWorkCenterCommand(
            created.Id, "Mill", "WC-MILL", null, 8m, 100m, 1, 40m, 20m, true, null, null, 1), CancellationToken.None);
        unchanged.BurdenBasis.Should().Be(BurdenBasis.Labor, "leaving it out keeps the current basis");
        (await db.WorkCenters.AsNoTracking().SingleAsync()).BurdenBasis.Should().Be(BurdenBasis.Labor);
    }

    [Fact]
    public void An_unknown_burden_basis_is_rejected()
    {
        var validator = new CreateWorkCenterValidator();

        validator.Validate(new CreateWorkCenterCommand("Mill", "WC-MILL", null, 8m, 100m, 1, 40m, 20m, null, null, 1, BurdenBasis: (BurdenBasis)7))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Operator_attendance_and_run_minutes_each_round_trip_on_the_routing_step()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-ATTEND", Name = "Shaft" };
        db.Add(part);
        await db.SaveChangesAsync();
        var repo = new PartRepository(db, Mock.Of<IPartPricingResolver>());

        var created = await new CreateOperationHandler(repo, Mock.Of<IVendorRepository>()).Handle(new CreateOperationCommand(part.Id,
            new CreateOperationRequestModel(10, "Turn", null, null, null, false, null, null, RunMinutesEach: 2.5m, LaborAttendancePercent: 20m)),
            CancellationToken.None);
        created.LaborAttendancePercent.Should().Be(20m);
        created.RunMinutesEach.Should().Be(2.5m);

        var defaulted = await new CreateOperationHandler(repo, Mock.Of<IVendorRepository>()).Handle(new CreateOperationCommand(part.Id,
            new CreateOperationRequestModel(20, "Deburr", null, null, null, false, null, null)), CancellationToken.None);
        defaulted.LaborAttendancePercent.Should().Be(100m);

        var updated = await new UpdateOperationHandler(repo, Mock.Of<IVendorRepository>()).Handle(new UpdateOperationCommand(part.Id, created.Id,
            new UpdateOperationRequestModel(null, null, null, null, null, null, null, null, LaborAttendancePercent: 35m)), CancellationToken.None);
        updated.LaborAttendancePercent.Should().Be(35m);
        updated.RunMinutesEach.Should().Be(2.5m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.5)]
    public void Operator_attendance_must_be_between_0_and_100(double percent)
    {
        new CreateOperationValidator().Validate(new CreateOperationCommand(1,
            new CreateOperationRequestModel(10, "Turn", null, null, null, false, null, null, LaborAttendancePercent: (decimal)percent)))
            .IsValid.Should().BeFalse();
    }
}
