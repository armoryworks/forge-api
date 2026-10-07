using System.Text.Json;

using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

using Forge.Api.Middleware;

namespace Forge.Tests.Middleware;

public sealed class ExceptionHandlingMiddlewareTests
{
    private static async Task<(int Status, JsonElement Body)> Run(Exception thrown)
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw thrown,
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(ctx);

        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        return (ctx.Response.StatusCode, doc.RootElement.Clone());
    }

    [Fact]
    public async Task Handler_validator_failures_use_the_model_binding_envelope()
    {
        var ex = new ValidationException(new[]
        {
            new ValidationFailure("Quantity", "Quantity must be greater than zero.") { AttemptedValue = 0m },
            new ValidationFailure("Lines[0].UnitPrice", "Unit price is required."),
        });

        var (status, body) = await Run(ex);

        status.Should().Be(StatusCodes.Status400BadRequest);
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("title").GetString().Should().Be("Validation failed");
        body.GetProperty("detail").GetString().Should().Be("Quantity must be greater than zero. Unit price is required.");

        var errors = body.GetProperty("errors");
        errors.GetArrayLength().Should().Be(2);
        errors[0].GetProperty("field").GetString().Should().Be("quantity");
        errors[0].GetProperty("message").GetString().Should().Be("Quantity must be greater than zero.");
        errors[0].GetProperty("rejectedValue").ValueKind.Should().Be(JsonValueKind.Null);
        errors[1].GetProperty("field").GetString().Should().Be("lines[0].UnitPrice");
        errors[1].GetProperty("rejectedValue").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Handler_validator_failures_never_echo_the_submitted_value()
    {
        var ex = new ValidationException(new[]
        {
            new ValidationFailure("Password", "Password must be at least 12 characters.") { AttemptedValue = "hunter2" },
        });

        var (_, body) = await Run(ex);

        body.GetProperty("errors")[0].GetProperty("rejectedValue").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetRawText().Should().NotContain("hunter2");
    }

    [Fact]
    public async Task Single_validator_failure_is_the_detail_verbatim()
    {
        var ex = new ValidationException(new[]
        {
            new ValidationFailure("Quantity", "Quantity must be greater than zero."),
        });

        var (_, body) = await Run(ex);

        body.GetProperty("detail").GetString().Should().Be("Quantity must be greater than zero.");
    }

    [Fact]
    public async Task Validation_failure_carries_its_message_in_detail()
    {
        const string message = "Part number 'P-1' belongs to a deleted part. Restore that part or choose another number.";

        var (_, body) = await Run(new ValidationException(message, [new ValidationFailure("partNumber", message)]));

        body.GetProperty("title").GetString().Should().Be("Validation failed");
        body.GetProperty("detail").GetString().Should().Be(message);
        body.GetProperty("errors")[0].GetProperty("field").GetString().Should().Be("partNumber");
        body.GetProperty("errors")[0].GetProperty("message").GetString().Should().Be(message);
    }

    [Fact]
    public async Task Several_failures_are_joined_once_each_in_detail()
    {
        var (_, body) = await Run(new ValidationException(
        [
            new ValidationFailure("name", "Name is required."),
            new ValidationFailure("name", "Name is required."),
            new ValidationFailure("revision", "Revision is too long."),
        ]));

        body.GetProperty("detail").GetString().Should().Be("Name is required. Revision is too long.");
    }

    [Fact]
    public async Task Bare_validation_exception_uses_its_message_as_the_detail()
    {
        var (status, body) = await Run(new ValidationException("Ship date cannot precede the order date."));

        status.Should().Be(StatusCodes.Status400BadRequest);
        body.GetProperty("detail").GetString().Should().Be("Ship date cannot precede the order date.");
        body.GetProperty("errors").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Business_rule_rejection_is_a_409_titled_action_not_allowed()
    {
        var (status, body) = await Run(new InvalidOperationException("Cannot delete order with active shipments"));

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("title").GetString().Should().Be("Action not allowed");
        body.GetProperty("detail").GetString().Should().Be("Cannot delete order with active shipments");
        body.GetProperty("code").GetString().Should().Be("business-rule");
    }

    [Fact]
    public async Task Document_number_collision_is_a_409_asking_to_save_again()
    {
        var pg = new PostgresException(
            "duplicate key value violates unique constraint", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
            constraintName: "ix_invoices_invoice_number");

        var (status, body) = await Run(new DbUpdateException("save failed", pg));

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("code").GetString().Should().Be("duplicate");
        body.GetProperty("detail").GetString()
            .Should().Be("That number was just taken by another record. Save again to get the next number.");
    }

    [Fact]
    public async Task Other_unique_violations_are_a_409_with_a_neutral_detail()
    {
        var pg = new PostgresException(
            "duplicate key value violates unique constraint", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
            constraintName: "ix_barcodes_value");

        var (status, body) = await Run(new DbUpdateException("save failed", pg));

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("code").GetString().Should().Be("duplicate");
        body.GetProperty("title").GetString().Should().Be("Duplicate value");
        body.GetProperty("detail").GetString().Should().Be("A record with that value already exists.");
    }

    [Fact]
    public async Task Unique_violation_without_a_constraint_name_gets_the_neutral_detail()
    {
        var pg = new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);

        var (_, body) = await Run(new DbUpdateException("save failed", pg));

        body.GetProperty("detail").GetString().Should().Be("A record with that value already exists.");
    }

    [Fact]
    public async Task Other_database_failures_stay_500()
    {
        var pg = new PostgresException("insert or update violates foreign key constraint", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation);

        var (status, body) = await Run(new DbUpdateException("save failed", pg));

        status.Should().Be(StatusCodes.Status500InternalServerError);
        body.TryGetProperty("code", out _).Should().BeFalse();
    }
}
