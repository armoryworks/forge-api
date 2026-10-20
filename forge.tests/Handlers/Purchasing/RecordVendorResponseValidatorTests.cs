using FluentAssertions;

using Forge.Api.Features.Purchasing;

namespace Forge.Tests.Handlers.Purchasing;

public class RecordVendorResponseValidatorTests
{
    private static bool IsValid(decimal? unitPrice = null, int? leadTimeDays = null, decimal? moq = null, decimal? tooling = null) =>
        new RecordVendorResponseValidator()
            .Validate(new RecordVendorResponseCommand(1, 2, unitPrice, leadTimeDays, moq, tooling, null, null))
            .IsValid;

    [Fact]
    public void A_response_with_no_figures_is_valid() => IsValid().Should().BeTrue();

    [Fact]
    public void A_realistic_quote_is_valid() => IsValid(12.345m, 14, 100m, 0m).Should().BeTrue();

    [Theory]
    [InlineData(-0.01, null, null, null)]
    [InlineData(null, -3, null, null)]
    [InlineData(null, null, 0d, null)]
    [InlineData(null, null, -1d, null)]
    [InlineData(null, null, null, -100d)]
    [InlineData(100000000000000d, null, null, null)]
    public void Negative_zero_moq_or_out_of_range_figures_are_rejected(double? unitPrice, int? leadTimeDays, double? moq, double? tooling) =>
        IsValid((decimal?)unitPrice, leadTimeDays, (decimal?)moq, (decimal?)tooling).Should().BeFalse();
}
