using FluentAssertions;

using Forge.Api.Features.CustomerTaxDocuments;
using Forge.Api.Features.Customers;
using Forge.Api.Features.Customers.Segments;
using Forge.Api.Features.PriceLists;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Customers;

public class CustomerInputValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("​​")]
    public void Create_rejects_a_blank_or_invisible_name(string name)
    {
        new CreateCustomerValidator().Validate(new CreateCustomerCommand(name, null, null, null))
            .IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ‍")]
    public void Update_rejects_a_blank_or_invisible_name(string name)
    {
        new UpdateCustomerValidator().Validate(new UpdateCustomerCommand(1, name, null, null, null, null))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Update_leaves_an_omitted_name_alone()
    {
        new UpdateCustomerValidator().Validate(new UpdateCustomerCommand(1, null, null, null, null, true))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("big spenders", false)]
    [InlineData("{\"minRevenue\": 1000", false)]
    [InlineData("{\"minRevenue\": 1000}", true)]
    [InlineData(null, true)]
    [InlineData("", true)]
    public void Segment_filter_criteria_must_be_json(string? criteria, bool valid)
    {
        new CreateCustomerSegmentValidator()
            .Validate(new CreateCustomerSegmentCommand(new CreateCustomerSegmentRequestModel("Key accounts", null, criteria)))
            .IsValid.Should().Be(valid);
        new UpdateCustomerSegmentValidator()
            .Validate(new UpdateCustomerSegmentCommand(1, new UpdateCustomerSegmentRequestModel("Key accounts", null, criteria, true)))
            .IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1440, true)]
    [InlineData(1441, false)]
    [InlineData(null, true)]
    public void Interaction_duration_is_bounded_to_a_day(int? minutes, bool valid)
    {
        new CreateContactInteractionValidator()
            .Validate(new CreateContactInteractionCommand(1, null, "Call", "Check-in", null, DateTimeOffset.UtcNow, minutes))
            .IsValid.Should().Be(valid);
        new UpdateContactInteractionValidator()
            .Validate(new UpdateContactInteractionCommand(1, 1, "Call", "Check-in", null, DateTimeOffset.UtcNow, minutes))
            .IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("UT", true)]
    [InlineData(" ut ", true)]
    [InlineData("U1", false)]
    [InlineData("U", false)]
    [InlineData("UTA", false)]
    public void Tax_document_state_is_two_letters(string state, bool valid)
    {
        new CreateCustomerTaxDocumentValidator()
            .Validate(new CreateCustomerTaxDocumentCommand(1, 1, state, "Resale", null, null))
            .IsValid.Should().Be(valid);
    }

    [Fact]
    public void Price_list_unit_price_stays_inside_the_column_precision()
    {
        new CreatePriceListEntryValidator()
            .Validate(new CreatePriceListEntryCommand(1, 1, 100_000_000_000_000m, 1, "USD", null))
            .IsValid.Should().BeFalse();
        new UpdatePriceListEntryValidator()
            .Validate(new UpdatePriceListEntryCommand(1, 100_000_000_000_000m, 1, "USD", null))
            .IsValid.Should().BeFalse();
        new CreatePriceListEntryValidator()
            .Validate(new CreatePriceListEntryCommand(1, 1, 1_000_000_000_000m, 1, "USD", null))
            .IsValid.Should().BeTrue();
    }
}
