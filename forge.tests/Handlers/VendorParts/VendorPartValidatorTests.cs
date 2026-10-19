using FluentAssertions;

using Forge.Api.Features.VendorParts;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.VendorParts;

public class VendorPartValidatorTests
{
    private static bool CreateIsValid(string? country, decimal? packSize) =>
        new CreateVendorPartValidator().Validate(new CreateVendorPartCommand(new CreateVendorPartRequestModel(
            VendorId: 1, PartId: 2, VendorPartNumber: "VP-1", ManufacturerName: null, VendorMpn: null,
            LeadTimeDays: null, MinOrderQty: 0m, PackSize: packSize, CountryOfOrigin: country, HtsCode: null,
            IsApproved: true, IsPreferred: false, Certifications: null, LastQuotedDate: null, Notes: null))).IsValid;

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("US", true)]
    [InlineData("de", true)]
    [InlineData("U", false)]
    [InlineData("U1", false)]
    [InlineData("1.", false)]
    public void Country_of_origin_must_be_two_letters(string? country, bool valid) =>
        CreateIsValid(country, null).Should().Be(valid);

    [Theory]
    [InlineData(null, true)]
    [InlineData(1.5555, true)]
    [InlineData(0d, false)]
    [InlineData(-2d, false)]
    public void Pack_size_must_be_positive(double? packSize, bool valid) =>
        CreateIsValid("US", (decimal?)packSize).Should().Be(valid);
}
