using FluentAssertions;

using Forge.Api.Features.Costing;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Costing;

/// <summary>
/// Tier-2 costing config surface — the Update handler upserts the "default" costing profile's mode +
/// departmental rates, and the Get handler reads them back parsed. Round-trips and mode transitions.
/// </summary>
public class CostingProfileHandlerTests
{
    [Fact]
    public async Task Update_then_Get_roundtrips_departmental_rates()
    {
        using var db = TestDbContextFactory.Create();
        var clock = new SystemClock();
        await new UpdateCostingProfileHandler(db, clock).Handle(
            new UpdateCostingProfileCommand("departmental", [new(3, 145m), new(7, 90m)]), CancellationToken.None);

        var result = await new GetCostingProfileHandler(db, clock).Handle(new GetCostingProfileQuery(), CancellationToken.None);

        result.Mode.Should().Be("departmental");
        result.DepartmentalRates.Should().HaveCount(2);
        result.DepartmentalRates.Should().ContainEquivalentOf(new DepartmentalRateModel(3, 145m));
        result.DepartmentalRates.Should().ContainEquivalentOf(new DepartmentalRateModel(7, 90m));
    }

    [Fact]
    public async Task Update_flat_clears_departmental_rates()
    {
        using var db = TestDbContextFactory.Create();
        var clock = new SystemClock();
        await new UpdateCostingProfileHandler(db, clock).Handle(
            new UpdateCostingProfileCommand("departmental", [new(3, 145m)]), CancellationToken.None);
        await new UpdateCostingProfileHandler(db, clock).Handle(
            new UpdateCostingProfileCommand("flat", []), CancellationToken.None);

        var result = await new GetCostingProfileHandler(db, clock).Handle(new GetCostingProfileQuery(), CancellationToken.None);

        result.Mode.Should().Be("flat");
        result.DepartmentalRates.Should().BeEmpty();
    }

    [Fact]
    public void Validator_rejects_unknown_mode()
    {
        // "abc" (Tier 3) is not a valid mode for the Tier-2 config surface.
        var result = new UpdateCostingProfileValidator().Validate(
            new UpdateCostingProfileCommand("abc", []));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_rejects_negative_rate()
    {
        var result = new UpdateCostingProfileValidator().Validate(
            new UpdateCostingProfileCommand("departmental", [new(3, -5m)]));
        result.IsValid.Should().BeFalse();
    }
}
