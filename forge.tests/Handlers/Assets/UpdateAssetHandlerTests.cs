using FluentAssertions;
using FluentValidation;

using Forge.Api.Features.Assets;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Assets;

public class UpdateAssetHandlerTests
{
    private static UpdateAssetRequestModel Patch(decimal? cost = null, DepreciationMethod? method = null, int? workCenterId = null) => new(
        null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
        AcquisitionCost: cost, DepreciationMethod: method, WorkCenterId: workCenterId);

    private static async Task<(AppDbContext Db, Asset Asset, WorkCenter WorkCenter)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        var workCenter = new WorkCenter { Code = "WC-PRESS", Name = "Press Line" };
        var asset = new Asset { Name = "Press 1", AssetType = AssetType.Machine, GlAccount = "1500" };
        db.AddRange(workCenter, asset);
        await db.SaveChangesAsync();
        return (db, asset, workCenter);
    }

    [Fact]
    public async Task Handle_UpdatesAcquisitionCostDepreciationAndWorkCenter_AndLeavesGlAccount()
    {
        var (db, asset, workCenter) = await SeedAsync();
        using var _ = db;
        var handler = new UpdateAssetHandler(new AssetRepository(db), db);

        var result = await handler.Handle(
            new UpdateAssetCommand(asset.Id, Patch(1500.99m, DepreciationMethod.StraightLine, workCenter.Id)),
            CancellationToken.None);

        result.AcquisitionCost.Should().Be(1500.99m);
        result.DepreciationMethod.Should().Be(DepreciationMethod.StraightLine);
        result.WorkCenterId.Should().Be(workCenter.Id);
        result.WorkCenterCode.Should().Be("WC-PRESS");
        result.GlAccount.Should().Be("1500");
    }

    [Fact]
    public async Task Handle_LeavesAcquisitionFieldsAlone_WhenOmitted()
    {
        var (db, asset, workCenter) = await SeedAsync();
        using var _ = db;
        asset.AcquisitionCost = 10m;
        asset.WorkCenterId = workCenter.Id;
        await db.SaveChangesAsync();
        var handler = new UpdateAssetHandler(new AssetRepository(db), db);

        var result = await handler.Handle(new UpdateAssetCommand(asset.Id, Patch()), CancellationToken.None);

        result.AcquisitionCost.Should().Be(10m);
        result.WorkCenterId.Should().Be(workCenter.Id);
    }

    [Fact]
    public async Task Handle_RejectsAnUnknownWorkCenter()
    {
        var (db, asset, _) = await SeedAsync();
        using var __ = db;
        var handler = new UpdateAssetHandler(new AssetRepository(db), db);

        var act = () => handler.Handle(new UpdateAssetCommand(asset.Id, Patch(workCenterId: 9999)), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == "workCenterId");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1500.999")]
    [InlineData("1000000001")]
    public void Validator_RejectsBadAcquisitionCost(string value)
    {
        var cost = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        new UpdateAssetCommandValidator().Validate(new UpdateAssetCommand(1, Patch(cost)))
            .Errors.Should().ContainSingle(e => e.PropertyName == "Data.AcquisitionCost");
    }

    [Fact]
    public void CreateValidator_RejectsAThirdDecimalOnAcquisitionCost()
    {
        var data = new CreateAssetRequestModel(
            "Press", AssetType.Machine, null, null, null, null, null, null, null, null, null, null,
            AcquisitionCost: 1500.999m);

        new CreateAssetCommandValidator().Validate(new CreateAssetCommand(data))
            .Errors.Should().ContainSingle(e => e.PropertyName == "Data.AcquisitionCost");
    }
}
