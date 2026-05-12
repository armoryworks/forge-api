using MediatR;

using Forge.Api.Behaviors;
using Forge.Api.Capabilities;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Phase 4 Phase-H — Unit tests for the MediatR pipeline-behavior side of the
/// capability gate. Verifies that requests carrying
/// <see cref="RequiresCapabilityAttribute"/> are short-circuited with
/// <see cref="CapabilityDisabledException"/> when the named capability is
/// disabled, that they pass through when enabled, and that requests with no
/// attribute are unaffected.
/// </summary>
public class CapabilityGateBehaviorTests
{
    [RequiresCapability("CAP-MD-CUSTOMERS")]
    private sealed record GatedRequest : IRequest<int>;

    private sealed record UngatedRequest : IRequest<int>;

    private sealed class StubSnapshotProvider(IReadOnlyDictionary<string, bool> state)
        : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } =
            new CapabilitySnapshot(state, DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);

        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Gated_Request_With_Disabled_Capability_Throws()
    {
        var snapshots = new StubSnapshotProvider(
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["CAP-MD-CUSTOMERS"] = false,
            });
        var behavior = new CapabilityGateBehavior<GatedRequest, int>(snapshots);

        var ex = await Assert.ThrowsAsync<CapabilityDisabledException>(() =>
            behavior.Handle(new GatedRequest(), _ => Task.FromResult(42), CancellationToken.None));

        Assert.Equal("CAP-MD-CUSTOMERS", ex.Capability);
    }

    [Fact]
    public async Task Gated_Request_With_Enabled_Capability_Passes_Through()
    {
        var snapshots = new StubSnapshotProvider(
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["CAP-MD-CUSTOMERS"] = true,
            });
        var behavior = new CapabilityGateBehavior<GatedRequest, int>(snapshots);

        var result = await behavior.Handle(
            new GatedRequest(),
            _ => Task.FromResult(42),
            CancellationToken.None);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Ungated_Request_Always_Passes_Through()
    {
        // Empty snapshot — nothing enabled — but the request type carries no
        // attribute so the gate must not interfere.
        var snapshots = new StubSnapshotProvider(
            new Dictionary<string, bool>(StringComparer.Ordinal));
        var behavior = new CapabilityGateBehavior<UngatedRequest, int>(snapshots);

        var result = await behavior.Handle(
            new UngatedRequest(),
            _ => Task.FromResult(99),
            CancellationToken.None);

        Assert.Equal(99, result);
    }

    [Fact]
    public void CapabilityDisabledException_Envelope_Matches_Middleware_Shape()
    {
        var ex = new CapabilityDisabledException("CAP-EXT-AI-ASSISTANT");
        var envelope = ex.ToEnvelope();

        // Same shape produced by CapabilityGateMiddleware so HttpErrorInterceptor
        // handles the response identically regardless of which gate fired.
        var json = System.Text.Json.JsonSerializer.Serialize(envelope, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        });

        Assert.Contains("\"code\":\"capability-disabled\"", json);
        Assert.Contains("\"capability\":\"CAP-EXT-AI-ASSISTANT\"", json);
        Assert.Contains("\"errors\":", json);
    }
}
