using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

using Forge.Api.Validation;

namespace Forge.Tests.Validation;

public class CustomInvalidModelStateResponseFactoryTests
{
    private static JsonElement Respond(ModelStateDictionary modelState)
    {
        var context = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), modelState);
        var result = CustomInvalidModelStateResponseFactory.Create()(context);
        var value = result.Should().BeOfType<BadRequestObjectResult>().Which.Value;
        return JsonSerializer.SerializeToElement(value);
    }

    [Fact]
    public void Body_required_noise_is_dropped_when_a_json_path_error_exists()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("request", "The request field is required.");
        modelState.AddModelError("$.numberOfMachines",
            "The JSON value could not be converted to System.Int32. Path: $.numberOfMachines | LineNumber: 0 | BytePositionInLine: 40.");

        var body = Respond(modelState);

        body.GetProperty("detail").GetString().Should().Be("Whole number is required");
        var errors = body.GetProperty("errors");
        errors.GetArrayLength().Should().Be(1);
        errors[0].GetProperty("field").GetString().Should().Be("numberOfMachines");
    }

    [Fact]
    public void Body_required_is_kept_when_it_is_the_only_error()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("request", "The request field is required.");

        Respond(modelState).GetProperty("detail").GetString().Should().Be("The request field is required");
    }

    [Fact]
    public void Controller_request_types_are_not_leaked()
    {
        var error = new ModelError(new JsonException(
            "The JSON value could not be converted to Forge.Api.Controllers.CreateWorkCenterRequest. Path: $ | LineNumber: 0 | BytePositionInLine: 1."),
            "The JSON value could not be converted to Forge.Api.Controllers.CreateWorkCenterRequest. Path: $ | LineNumber: 0 | BytePositionInLine: 1.");

        CustomInvalidModelStateResponseFactory.HumanizeError(error)
            .Should().Be("One or more fields contain an invalid value");
    }

    [Theory]
    [InlineData("Data.Title", "Title")]
    [InlineData("Request.Description", "Description")]
    [InlineData("Body.Lines[0].Quantity", "Lines[0].Quantity")]
    [InlineData("Model.Name", "Name")]
    [InlineData("Data", "Data")]
    [InlineData("Quantity", "Quantity")]
    public void Command_wrapper_is_stripped_from_validator_property_names(string raw, string expected) =>
        CustomInvalidModelStateResponseFactory.StripCommandWrapper(raw).Should().Be(expected);
}
