using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;

using QBEngineer.Api.Features.Communications;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models.Communications;
using QBEngineer.Tests.Helpers;

namespace QBEngineer.Tests.Handlers.Communications;

/// <summary>
/// Wave 8 phase 1k.2 — OAuth begin + complete handler tests. Drive a
/// fake <see cref="IImapOAuthService"/> so the state-token lifecycle,
/// CSRF guards, and DB persistence are exercised without HTTP I/O.
/// </summary>
public class OAuthImapHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly EphemeralDataProtectionProvider _dataProtection = new();
    private readonly FixedClock _clock = new();
    private readonly StubOAuthService _oauth = new();
    private const int UserA = 42;

    public OAuthImapHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _db.CurrentUserId = UserA;
    }

    private BeginOAuthImapHandler MakeBeginHandler() => new(_db, _oauth, _clock);
    private CompleteOAuthImapHandler MakeCompleteHandler() => new(_db, _oauth, _dataProtection, _clock);

    [Fact]
    public async Task Begin_GeneratesStateToken_AndReturnsAuthorizeUrl()
    {
        var handler = MakeBeginHandler();
        var result = await handler.Handle(new BeginOAuthImapCommand("google"), CancellationToken.None);

        result.AuthorizeUrl.Should().Contain($"state={result.State}");
        result.State.Should().HaveLength(64); // 32-byte hex

        var saved = _db.OAuthStateTokens.Single();
        saved.UserId.Should().Be(UserA);
        saved.ProviderKey.Should().Be("google");
        saved.Token.Should().Be(result.State);
        saved.ExpiresAt.Should().BeAfter(_clock.UtcNow);
    }

    [Fact]
    public async Task Begin_ThrowsWhenProviderUnconfigured()
    {
        _oauth.IsConfigured = false;
        var act = async () => await MakeBeginHandler().Handle(
            new BeginOAuthImapCommand("google"), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not configured*");
    }

    [Fact]
    public async Task Complete_PersistsConnection_WithSealedTokens()
    {
        // Seed state token via the begin path so the round-trip is complete.
        var begin = await MakeBeginHandler().Handle(
            new BeginOAuthImapCommand("google"), CancellationToken.None);

        _oauth.NextTokens = new OAuthTokenResult(
            "access-1", "refresh-1", _clock.UtcNow.AddSeconds(3600), "user@example.com");

        var result = await MakeCompleteHandler().Handle(
            new CompleteOAuthImapCommand("google", "code-1", begin.State), CancellationToken.None);

        result.IsConnected.Should().BeTrue();
        result.ProviderId.Should().Be("imap");
        result.ExternalAccountId.Should().Be("user@example.com");

        var saved = _db.CommunicationSyncConfigs.Single();
        var protector = _dataProtection.CreateProtector("communication-sync.imap");
        protector.Unprotect(saved.AccessToken!).Should().Be("access-1");
        protector.Unprotect(saved.RefreshToken!).Should().Be("refresh-1");
        saved.AccessTokenExpiresAt.Should().Be(_clock.UtcNow.AddSeconds(3600));

        var config = JsonSerializer.Deserialize<ImapConnectionConfig>(saved.ConfigJson!)!;
        config.AuthMethod.Should().Be("oauth");
        config.OAuthProvider.Should().Be("google");
        config.Host.Should().Be("imap.gmail.com");
        config.Username.Should().Be("user@example.com");

        // State token consumed.
        _db.OAuthStateTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Complete_RejectsForeignStateToken_CsrfGuard()
    {
        // User B initiated the OAuth flow; user A tries to complete it.
        // The handler must reject — the state token is bound to user B.
        _db.CurrentUserId = 99; // user B
        var begin = await MakeBeginHandler().Handle(
            new BeginOAuthImapCommand("google"), CancellationToken.None);

        _db.CurrentUserId = UserA; // attacker
        _oauth.NextTokens = new OAuthTokenResult("a", "r", _clock.UtcNow.AddSeconds(3600), "x@y.com");

        var act = async () => await MakeCompleteHandler().Handle(
            new CompleteOAuthImapCommand("google", "code", begin.State), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid OAuth state*");
    }

    [Fact]
    public async Task Complete_RejectsExpiredStateToken()
    {
        var begin = await MakeBeginHandler().Handle(
            new BeginOAuthImapCommand("google"), CancellationToken.None);

        // Advance clock past the 10-minute window.
        _clock.UtcNow = _clock.UtcNow.AddMinutes(11);
        _oauth.NextTokens = new OAuthTokenResult("a", "r", _clock.UtcNow.AddSeconds(3600), "x@y.com");

        var act = async () => await MakeCompleteHandler().Handle(
            new CompleteOAuthImapCommand("google", "code", begin.State), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expired*");
        // Expired token cleaned up.
        _db.OAuthStateTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Complete_RejectsCrossProviderState()
    {
        // Begin'd for google; complete tries to use the same state for microsoft.
        var begin = await MakeBeginHandler().Handle(
            new BeginOAuthImapCommand("google"), CancellationToken.None);

        _oauth.NextTokens = new OAuthTokenResult("a", "r", _clock.UtcNow.AddSeconds(3600), "x@y.com");

        var act = async () => await MakeCompleteHandler().Handle(
            new CompleteOAuthImapCommand("microsoft", "code", begin.State), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid OAuth state*");
    }

    [Fact]
    public async Task Complete_RejectsDuplicateConnectionForSameEmail()
    {
        _db.CommunicationSyncConfigs.Add(new CommunicationSyncConfig
        {
            UserId = UserA, Kind = CommunicationKind.Email, ProviderId = "imap",
            ExternalAccountId = "user@example.com", IsConnected = true,
        });
        await _db.SaveChangesAsync();

        var begin = await MakeBeginHandler().Handle(
            new BeginOAuthImapCommand("google"), CancellationToken.None);
        _oauth.NextTokens = new OAuthTokenResult("a", "r", _clock.UtcNow.AddSeconds(3600), "user@example.com");

        var act = async () => await MakeCompleteHandler().Handle(
            new CompleteOAuthImapCommand("google", "code", begin.State), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Fact]
    public async Task Complete_StateTokenConsumedBeforeExternalIO()
    {
        // If the OAuth exchange throws AFTER state is consumed, retrying
        // the same code yields the same "invalid state" error rather than
        // letting the attacker replay. The handler removes the row + saves
        // BEFORE the external token-exchange call.
        var begin = await MakeBeginHandler().Handle(
            new BeginOAuthImapCommand("google"), CancellationToken.None);
        _oauth.ThrowOnExchange = new InvalidOperationException("provider down");

        var act = async () => await MakeCompleteHandler().Handle(
            new CompleteOAuthImapCommand("google", "code", begin.State), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*provider down*");

        _db.OAuthStateTokens.Should().BeEmpty(); // consumed before the throw
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class StubOAuthService : IImapOAuthService
    {
        public bool IsConfigured { get; set; } = true;
        public OAuthTokenResult NextTokens { get; set; } =
            new("a", "r", DateTimeOffset.UtcNow, "x@y.com");
        public Exception? ThrowOnExchange { get; set; }

        public bool IsProviderConfigured(string providerKey) => IsConfigured;

        public string BuildAuthorizeUrl(string providerKey, string state)
            => $"https://provider/auth?state={state}";

        public Task<OAuthTokenResult> ExchangeCodeForTokensAsync(
            string providerKey, string code, CancellationToken ct)
        {
            if (ThrowOnExchange is not null) throw ThrowOnExchange;
            return Task.FromResult(NextTokens);
        }

        public Task<OAuthRefreshResult> RefreshAccessTokenAsync(
            string providerKey, string refreshToken, CancellationToken ct)
            => Task.FromResult(new OAuthRefreshResult("a", null, DateTimeOffset.UtcNow));
    }
}
