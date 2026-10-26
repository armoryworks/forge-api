using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Capabilities;

namespace Forge.Tests.Capabilities;

/// <summary>
/// The controller-side gate reads <see cref="RequiresAnyCapabilityAttribute"/>: the request passes when any one of
/// the listed capabilities is on and is refused with the 403 envelope naming the first code when none is.
/// </summary>
public class CapabilityGateMiddlewareAnyOfTests
{
    private sealed class StubSnapshotProvider(IReadOnlyDictionary<string, bool> state) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(state, DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);

        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static async Task<(bool Reached, HttpContext Context)> RunAsync(Dictionary<string, bool> state)
    {
        var reached = false;
        var middleware = new CapabilityGateMiddleware(
            _ => { reached = true; return Task.CompletedTask; },
            NullLogger<CapabilityGateMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            null,
            new EndpointMetadataCollection(new RequiresAnyCapabilityAttribute("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")),
            "stock-loads"));

        await middleware.InvokeAsync(context, new StubSnapshotProvider(state));
        return (reached, context);
    }

    [Fact]
    public async Task Any_of_endpoint_passes_when_one_capability_is_on()
    {
        var (reached, context) = await RunAsync(new(StringComparer.Ordinal)
        {
            ["CAP-ACCT-MIGRATION"] = false,
            ["CAP-ACCT-FULLGL"] = true,
        });

        Assert.True(reached);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Any_of_endpoint_is_refused_naming_the_first_code_when_none_is_on()
    {
        var (reached, context) = await RunAsync(new(StringComparer.Ordinal)
        {
            ["CAP-ACCT-MIGRATION"] = false,
            ["CAP-ACCT-FULLGL"] = false,
        });

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("CAP-ACCT-MIGRATION", context.Response.Headers["X-Capability-Disabled"].ToString());
    }
}
