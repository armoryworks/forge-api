using FluentAssertions;

using Forge.Api.Features.Jobs;
using Forge.Core.Entities;

namespace Forge.Tests.Handlers.Jobs;

public class JobOverrunTests
{
    [Theory]
    [InlineData(40d, null, null)]
    [InlineData(40d, 0, 40d)]
    [InlineData(40d, 10, 44d)]
    [InlineData(7d, 10, 7.7d)]
    [InlineData(3d, 33, 3.99d)]
    public void Limit_is_the_target_plus_the_percentage_or_none_when_unset(double target, int? percent, double? expected)
        => JobOverrun.Limit((decimal)target, percent).Should().Be(expected is null ? null : (decimal)expected);

    [Fact]
    public void Target_is_the_order_demand_when_set_and_never_more_than_the_build_quantity()
    {
        JobOverrun.TargetQuantity(new Job { TargetQuantity = 100m }, 110m).Should().Be(100m);
        JobOverrun.TargetQuantity(new Job(), 110m).Should().Be(110m);
        JobOverrun.TargetQuantity(new Job { TargetQuantity = 120m }, 110m).Should().Be(110m);
    }

    [Fact]
    public void Note_names_only_good_pieces_over_the_target()
    {
        JobOverrun.Note(40m, 40m).Should().BeEmpty();
        JobOverrun.Note(40m, 43m).Should().Be(" Overrun: 3 good over the start quantity of 40.");
    }
}
