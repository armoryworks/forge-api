using System.Text.Json;

using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Middleware;

namespace Forge.Tests.Middleware;

public sealed class ExceptionHandlingMiddlewareTests
{
    private static async Task<JsonElement> InvokeThrowing(Exception ex)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw ex, NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Validation_failure_carries_its_message_in_detail()
    {
        const string message = "Part number 'P-1' belongs to a deleted part. Restore that part or choose another number.";

        var body = await InvokeThrowing(new ValidationException(message, [new ValidationFailure("partNumber", message)]));

        body.GetProperty("title").GetString().Should().Be("Validation failed");
        body.GetProperty("detail").GetString().Should().Be(message);
        body.GetProperty("errors").GetProperty("partNumber")[0].GetString().Should().Be(message);
    }

    [Fact]
    public async Task Several_failures_are_joined_once_each_in_detail()
    {
        var body = await InvokeThrowing(new ValidationException(
        [
            new ValidationFailure("name", "Name is required."),
            new ValidationFailure("name", "Name is required."),
            new ValidationFailure("revision", "Revision is too long."),
        ]));

        body.GetProperty("detail").GetString().Should().Be("Name is required. Revision is too long.");
    }
}
