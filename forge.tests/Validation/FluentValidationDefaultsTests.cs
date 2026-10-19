using FluentAssertions;
using FluentValidation;

using Forge.Api.Validation;

namespace Forge.Tests.Validation;

public class FluentValidationDefaultsTests
{
    public sealed record Payload(string? Title, int Quantity, DateTimeOffset BillDate, DateTimeOffset DueDate, int PartId);

    public sealed record WrappedCommand(Payload Data);

    private sealed class WrappedValidator : AbstractValidator<WrappedCommand>
    {
        public WrappedValidator()
        {
            RuleFor(x => x.Data.Title).NotEmpty().MaximumLength(5);
            RuleFor(x => x.Data.Quantity).GreaterThanOrEqualTo(1);
            RuleFor(x => x.Data.PartId).GreaterThan(0);
        }
    }

    public FluentValidationDefaultsTests() => FluentValidationDefaults.Apply();

    [Fact]
    public void Messages_use_the_member_name_unquoted_without_the_wrapper()
    {
        var result = new WrappedValidator().Validate(
            new WrappedCommand(new Payload(null, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0)));

        result.Errors.Select(e => e.ErrorMessage).Should().BeEquivalentTo(
            "Title is required.",
            "Quantity must be at least 1.",
            "Part ID must be greater than 0.");
        result.Errors.Select(e => e.PropertyName).Should().Contain("Data.Title");
    }

    [Fact]
    public void Maximum_length_message_reads_plainly()
    {
        var result = new WrappedValidator().Validate(
            new WrappedCommand(new Payload("Too long", 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1)));

        result.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Be("Title must be 5 characters or fewer.");
    }

    [Theory]
    [InlineData("DueDate", "Due date")]
    [InlineData("PartId", "Part ID")]
    [InlineData("SKUCode", "SKU code")]
    [InlineData("Name", "Name")]
    public void Display_names_are_sentence_case(string member, string expected) =>
        FluentValidationDefaults.ToDisplayName(member).Should().Be(expected);
}
