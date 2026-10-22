using FluentAssertions;

using Forge.Api.Features.Costing.Tier3;
using Forge.Api.Features.Parts;
using Forge.Api.Validation;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Parts;

public class NumericColumnLimitTests
{
    private static CreateOperationRequestModel CreateOp(decimal? setup = null, decimal? lot = null, decimal? turnDays = null, long? estimatedMs = null) => new(
        StepNumber: 1, Title: "Mill", Instructions: null, WorkCenterId: null,
        EstimatedMs: estimatedMs, IsQcCheckpoint: false, QcCriteria: null, ReferencedOperationId: null,
        IsSubcontract: turnDays.HasValue ? true : null, SubcontractVendorId: turnDays.HasValue ? 7 : null,
        SubcontractTurnTimeDays: turnDays, SetupMinutes: setup, RunMinutesLot: lot);

    private static UpdateOperationRequestModel UpdateOp(decimal? setup = null, decimal? lot = null, long? estimatedMs = null) => new(
        StepNumber: null, Title: null, Instructions: null, WorkCenterId: null,
        EstimatedMs: estimatedMs, IsQcCheckpoint: null, QcCriteria: null,
        ReferencedOperationId: null, SetupMinutes: setup, RunMinutesLot: lot);

    private static CreateCostingCostCenterCommand CostCenter(decimal? sqft, decimal? headcount = null)
        => new("CC-1", "Molding", CostCenterType.Production, null, sqft, headcount, true);

    [Fact]
    public void MaxValue_MatchesColumnBounds()
    {
        NumericColumnRules.MaxValue(10, 2).Should().Be(99_999_999.99m);
        NumericColumnRules.MaxValue(18, 4).Should().Be(99_999_999_999_999.9999m);
        NumericColumnRules.MaxValue(5, 1).Should().Be(9_999.9m);
    }

    [Theory]
    [InlineData("1000000000000", "Data.SetupMinutes")]
    [InlineData("100000000", "Data.SetupMinutes")]
    [InlineData("12.345", "Data.SetupMinutes")]
    public void CreateOperation_RejectsSetupMinutesThatOverflowTheColumn(string value, string property)
    {
        var result = new CreateOperationValidator().Validate(
            new CreateOperationCommand(1, CreateOp(setup: decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture))));

        result.Errors.Should().ContainSingle(e => e.PropertyName == property)
            .Which.ErrorMessage.Should().Contain("99,999,999.99").And.Contain("2 decimal places");
    }

    [Fact]
    public void CreateOperation_RejectsLotMinutesAndTurnTimeThatOverflow()
    {
        var result = new CreateOperationValidator().Validate(
            new CreateOperationCommand(1, CreateOp(lot: 1e12m, turnDays: 10_000m)));

        result.Errors.Select(e => e.PropertyName).Should()
            .BeEquivalentTo(["Data.RunMinutesLot", "Data.SubcontractTurnTimeDays"]);
    }

    [Fact]
    public void UpdateOperation_RejectsOverflowingMinutes()
    {
        var result = new UpdateOperationValidator().Validate(
            new UpdateOperationCommand(1, 1, UpdateOp(setup: 1e12m, lot: 0.001m)));

        result.Errors.Select(e => e.PropertyName).Should()
            .BeEquivalentTo(["Data.SetupMinutes", "Data.RunMinutesLot"]);
    }

    [Fact]
    public void Operations_AcceptTheLargestValueAndTrailingZeros()
    {
        new CreateOperationValidator().Validate(new CreateOperationCommand(1, CreateOp(setup: 99_999_999.99m, lot: 1.500m)))
            .IsValid.Should().BeTrue();
        new UpdateOperationValidator().Validate(new UpdateOperationCommand(1, 1, UpdateOp(setup: 99_999_999.99m, lot: 1.500m)))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("0.333333", false)]
    [InlineData("0.3333", true)]
    [InlineData("1.50", true)]
    [InlineData("1.500000", true)]
    [InlineData("100000000000000", false)]
    public void BomLineQuantity_MustFitNumeric18_4(string value, bool valid)
    {
        var quantity = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        new CreateBOMLineCommandValidator()
            .Validate(new CreateBOMLineCommand(1, new CreateBOMLineRequestModel(2, quantity, null, BOMSourceType.Stock, null, null)))
            .IsValid.Should().Be(valid);
        new UpdateBOMLineValidator()
            .Validate(new UpdateBOMLineCommand(1, 1, new UpdateBOMLineRequestModel(quantity, null, null, null, null)))
            .IsValid.Should().Be(valid);
    }

    [Fact]
    public void CostCenter_RejectsNegativeSquareFeetAndHeadcount()
    {
        var result = new CreateCostingCostCenterValidator().Validate(CostCenter(-500m, -1m));

        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Sqft", "Headcount"]);
    }

    [Fact]
    public void CostCenter_RejectsSquareFeetThatOverflow()
    {
        new CreateCostingCostCenterValidator().Validate(CostCenter(1e15m))
            .Errors.Should().ContainSingle(e => e.PropertyName == "Sqft");
    }

    [Fact]
    public void CostCenter_AcceptsZeroAndBlankAllocationBases()
    {
        new CreateCostingCostCenterValidator().Validate(CostCenter(0m, 0m)).IsValid.Should().BeTrue();
        new CreateCostingCostCenterValidator().Validate(CostCenter(null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void MaxOperationEstimatedMs_IsTheLargestEstimateWhosePerPieceMinutesFitTheJobStep()
    {
        var max = NumericColumnRules.MaxOperationEstimatedMs;

        (max / 60000m).Should().BeLessThanOrEqualTo(NumericColumnRules.MaxValue(14, 6));
        ((max + 1) / 60000m).Should().BeGreaterThan(NumericColumnRules.MaxValue(14, 6));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void CreateOperation_RejectsEstimatedMsOutOfRange(long estimatedMs)
    {
        var result = new CreateOperationValidator().Validate(new CreateOperationCommand(1, CreateOp(estimatedMs: estimatedMs)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Data.EstimatedMs");
    }

    [Fact]
    public void CreateOperation_AcceptsEstimatedMsAtTheBounds()
    {
        var validator = new CreateOperationValidator();

        validator.Validate(new CreateOperationCommand(1, CreateOp(estimatedMs: 0L))).IsValid.Should().BeTrue();
        validator.Validate(new CreateOperationCommand(1, CreateOp(estimatedMs: NumericColumnRules.MaxOperationEstimatedMs))).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void UpdateOperation_RejectsEstimatedMsOutOfRange(long estimatedMs)
    {
        var result = new UpdateOperationValidator().Validate(new UpdateOperationCommand(1, 2, UpdateOp(estimatedMs: estimatedMs)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Data.EstimatedMs");
    }

    [Fact]
    public void UpdateOperation_AcceptsEstimatedMsAtTheMaximum()
    {
        new UpdateOperationValidator()
            .Validate(new UpdateOperationCommand(1, 2, UpdateOp(estimatedMs: NumericColumnRules.MaxOperationEstimatedMs)))
            .IsValid.Should().BeTrue();
    }
}
