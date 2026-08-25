using System.Text;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Middleware;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Middleware;

[Collection(PostgresCollection.Name)]
public sealed class IdempotencyMiddlewareTests(PostgresFixture fixture)
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;
    }

    private static DefaultHttpContext Request(string key, string body)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/v1/jobs/7/notes";
        ctx.Request.Headers[IdempotencyMiddleware.HeaderName] = key;
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static async Task<string> BodyOf(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        return await new StreamReader(ctx.Response.Body).ReadToEndAsync();
    }

    [Fact]
    public async Task Replay_returns_the_stored_response_without_re_executing()
    {
        var executions = 0;
        var middleware = new IdempotencyMiddleware(async ctx =>
        {
            executions++;
            ctx.Response.StatusCode = 201;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"id\":42}");
        }, NullLogger<IdempotencyMiddleware>.Instance);

        var key = Guid.NewGuid().ToString();
        await middleware.InvokeAsync(Request(key, "{\"text\":\"hello\"}"), fixture.CreateContext(), new FixedClock());
        var second = Request(key, "{\"text\":\"hello\"}");
        await middleware.InvokeAsync(second, fixture.CreateContext(), new FixedClock());

        executions.Should().Be(1);
        second.Response.StatusCode.Should().Be(201);
        second.Response.Headers["Idempotent-Replayed"].ToString().Should().Be("true");
        (await BodyOf(second)).Should().Be("{\"id\":42}");
    }

    [Fact]
    public async Task Same_key_with_a_different_body_is_refused()
    {
        var middleware = new IdempotencyMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = 201;
            await ctx.Response.WriteAsync("ok");
        }, NullLogger<IdempotencyMiddleware>.Instance);

        var key = Guid.NewGuid().ToString();
        await middleware.InvokeAsync(Request(key, "a"), fixture.CreateContext(), new FixedClock());
        var second = Request(key, "b");
        await middleware.InvokeAsync(second, fixture.CreateContext(), new FixedClock());

        second.Response.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Server_errors_are_not_stored_so_the_client_can_retry()
    {
        var executions = 0;
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            executions++;
            ctx.Response.StatusCode = executions == 1 ? 503 : 200;
            return Task.CompletedTask;
        }, NullLogger<IdempotencyMiddleware>.Instance);

        var key = Guid.NewGuid().ToString();
        await middleware.InvokeAsync(Request(key, "x"), fixture.CreateContext(), new FixedClock());
        var second = Request(key, "x");
        await middleware.InvokeAsync(second, fixture.CreateContext(), new FixedClock());

        executions.Should().Be(2);
        second.Response.StatusCode.Should().Be(200);
    }
}
