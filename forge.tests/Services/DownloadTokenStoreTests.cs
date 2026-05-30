using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Interfaces;

namespace Forge.Tests.Services;

/// <summary>
/// Same shape as <see cref="SsoHandoffStoreTests"/> — the
/// <see cref="DownloadTokenStore"/> is the SSO handoff pattern reused for
/// the RFID-relay installer, and its tests mirror that one's invariants
/// (single-use, expiry, distinct URL-safe codes) so any regression on
/// either side is obvious.
/// </summary>
public class DownloadTokenStoreTests
{
    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void Issue_ThenConsume_ReturnsBoundUserId()
    {
        var store = new DownloadTokenStore(new MutableClock());

        var token = store.Issue(userId: 42);
        var consumed = store.Consume(token);

        consumed.Should().Be(42);
    }

    [Fact]
    public void Consume_IsSingleUse()
    {
        var store = new DownloadTokenStore(new MutableClock());
        var token = store.Issue(userId: 7);

        store.Consume(token).Should().Be(7);
        store.Consume(token).Should().BeNull("a download token must be consumable exactly once");
    }

    [Theory]
    [InlineData("")]
    [InlineData("never-issued")]
    [InlineData("dlt_bogus")]
    public void Consume_UnknownOrEmptyToken_ReturnsNull(string token)
    {
        var store = new DownloadTokenStore(new MutableClock());
        store.Consume(token).Should().BeNull();
    }

    [Fact]
    public void Consume_AfterExpiry_ReturnsNull()
    {
        var clock = new MutableClock();
        var store = new DownloadTokenStore(clock);
        var token = store.Issue(userId: 11);

        // Past the 30-minute TTL.
        clock.UtcNow = clock.UtcNow.AddMinutes(31);

        store.Consume(token).Should().BeNull(
            "an expired download token must not unlock the installer endpoint");
    }

    [Fact]
    public void Issue_ReturnsDistinctUrlSafeTokensWithDltPrefix()
    {
        var store = new DownloadTokenStore(new MutableClock());

        var a = store.Issue(userId: 1);
        var b = store.Issue(userId: 1);

        a.Should().NotBe(b);
        foreach (var token in new[] { a, b })
        {
            token.Should().StartWith("dlt_",
                "the dlt_ prefix is how operators recognise download tokens in logs");
            token.Should().NotContainAny("+", "/", "=",
                "tokens travel in HTTP headers and must be URL-safe");
        }
    }
}
