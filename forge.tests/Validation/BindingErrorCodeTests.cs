using FluentAssertions;

using Forge.Api.Validation;

namespace Forge.Tests.Validation;

public sealed class BindingErrorCodeTests
{
    [Theory]
    [InlineData("Date is not valid", "invalid-date")]
    [InlineData("Number is not valid", "invalid-number")]
    [InlineData("Whole number is required", "whole-number-required")]
    [InlineData("True or false expected", "invalid-boolean")]
    [InlineData("Identifier is not valid", "invalid-identifier")]
    [InlineData("Value is not one of the allowed values", "invalid-option")]
    [InlineData("Request body is required", "body-required")]
    [InlineData("Something the binder said", "invalid-value")]
    public void Each_humanized_binding_error_has_a_stable_code(string message, string code)
    {
        CustomInvalidModelStateResponseFactory.BindingErrorCode(message).Should().Be(code);
    }
}
