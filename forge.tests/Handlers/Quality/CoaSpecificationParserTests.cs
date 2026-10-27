using FluentAssertions;

using Forge.Api.Features.Quality.Coa;

namespace Forge.Tests.Handlers.Quality;

public sealed class CoaSpecificationParserTests
{
    [Theory]
    [InlineData("10 ± 0.5", 9.5, 10.5, 10.0)]
    [InlineData("10 +/- 0.5 mm", 9.5, 10.5, 10.0)]
    [InlineData("9.9 - 10.1", 9.9, 10.1, null)]
    [InlineData("9.9–10.1", 9.9, 10.1, null)]
    [InlineData("2 to 4", 2.0, 4.0, null)]
    [InlineData("<= 5", null, 5.0, null)]
    [InlineData("≤ 0.02", null, 0.02, null)]
    [InlineData("Max 7", null, 7.0, null)]
    [InlineData(">= 2", 2.0, null, null)]
    [InlineData("min 1.5", 1.5, null, null)]
    public void Reads_numeric_limits(string specification, double? min, double? max, double? target)
    {
        var parsed = CoaSpecificationParser.Parse(specification);

        parsed.Min.Should().Be((decimal?)min);
        parsed.Max.Should().Be((decimal?)max);
        parsed.Target.Should().Be((decimal?)target);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Natural, no specks")]
    public void Text_only_specifications_have_no_limits(string? specification)
        => CoaSpecificationParser.Parse(specification).Should().Be(((decimal?)null, (decimal?)null, (decimal?)null));
}
