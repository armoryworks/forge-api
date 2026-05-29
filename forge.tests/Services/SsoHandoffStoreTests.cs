using FluentAssertions;

using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Interfaces;

namespace Forge.Tests.Services;

public class SsoHandoffStoreTests
{
    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);
    }

    private static LoginResponse SampleResponse(string token = "forge-jwt") =>
        new(token, DateTimeOffset.UtcNow.AddHours(1),
            new AuthUserResponseModel(7, "jane@example.com", "Jane", "Doe", "JD", "#abc", ["Engineer"], true));

    [Fact]
    public void Create_ThenConsume_ReturnsSameResponse()
    {
        var store = new SsoHandoffStore(new MutableClock());
        var response = SampleResponse();

        var code = store.Create(response);
        var consumed = store.Consume(code);

        consumed.Should().BeSameAs(response);
    }

    [Fact]
    public void Consume_IsSingleUse()
    {
        var store = new SsoHandoffStore(new MutableClock());
        var code = store.Create(SampleResponse());

        store.Consume(code).Should().NotBeNull();
        store.Consume(code).Should().BeNull("a handoff code must be consumable exactly once");
    }

    [Theory]
    [InlineData("")]
    [InlineData("never-issued")]
    public void Consume_UnknownOrEmptyCode_ReturnsNull(string code)
    {
        var store = new SsoHandoffStore(new MutableClock());
        store.Consume(code).Should().BeNull();
    }

    [Fact]
    public void Consume_AfterExpiry_ReturnsNull()
    {
        var clock = new MutableClock();
        var store = new SsoHandoffStore(clock);
        var code = store.Create(SampleResponse());

        // Past the 60s TTL.
        clock.UtcNow = clock.UtcNow.AddSeconds(61);

        store.Consume(code).Should().BeNull("an expired handoff code must not yield a session");
    }

    [Fact]
    public void Create_ReturnsDistinctUrlSafeCodes()
    {
        var store = new SsoHandoffStore(new MutableClock());

        var a = store.Create(SampleResponse("a"));
        var b = store.Create(SampleResponse("b"));

        a.Should().NotBe(b);
        foreach (var code in new[] { a, b })
        {
            code.Should().NotContainAny("+", "/", "=",
                "codes ride in a URL query param and must be URL-safe");
        }
    }
}
