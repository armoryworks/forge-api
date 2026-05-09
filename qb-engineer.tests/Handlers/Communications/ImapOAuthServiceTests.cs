using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using QBEngineer.Api.Features.Communications;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models.Communications;

namespace QBEngineer.Tests.Handlers.Communications;

/// <summary>
/// Wave 8 phase 1k.2 — OAuth service tests. Drive a real
/// <see cref="ImapOAuthService"/> with a fake <see cref="HttpMessageHandler"/>
/// so the URL construction, code exchange, and refresh logic are
/// exercised against canned token responses without touching real
/// Google / Microsoft endpoints.
/// </summary>
public class ImapOAuthServiceTests
{
    private const string GoogleClientId = "google-client";
    private const string GoogleSecret = "google-secret";
    private const string MsClientId = "ms-client";
    private const string MsSecret = "ms-secret";
    private const string Redirect = "https://app.test/account/communications/oauth-callback";

    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero));

    private ImapOAuthService MakeService(StubHttpHandler? handler = null, OAuthImapOptions? options = null)
    {
        var opts = options ?? new OAuthImapOptions
        {
            RedirectUri = Redirect,
            Google = new() { ClientId = GoogleClientId, ClientSecret = GoogleSecret },
            Microsoft = new() { ClientId = MsClientId, ClientSecret = MsSecret },
        };
        var http = new HttpClient(handler ?? new StubHttpHandler());
        return new ImapOAuthService(
            http, Options.Create(opts), _clock, NullLogger<ImapOAuthService>.Instance);
    }

    [Fact]
    public void IsProviderConfigured_RequiresBothCredAndRedirect()
    {
        var configured = MakeService();
        configured.IsProviderConfigured("google").Should().BeTrue();
        configured.IsProviderConfigured("microsoft").Should().BeTrue();
        configured.IsProviderConfigured("yahoo").Should().BeFalse(); // unknown

        var noClientId = MakeService(options: new OAuthImapOptions
        {
            RedirectUri = Redirect,
            Google = new() { ClientId = null, ClientSecret = "x" },
        });
        noClientId.IsProviderConfigured("google").Should().BeFalse();

        var noRedirect = MakeService(options: new OAuthImapOptions
        {
            RedirectUri = string.Empty,
            Google = new() { ClientId = "x", ClientSecret = "y" },
        });
        noRedirect.IsProviderConfigured("google").Should().BeFalse();
    }

    [Fact]
    public void BuildAuthorizeUrl_ContainsAllRequiredParams_Google()
    {
        var url = MakeService().BuildAuthorizeUrl("google", "state-abc");

        url.Should().StartWith(ImapOAuthProvider.Google.AuthorizeUrl);
        url.Should().Contain($"client_id={GoogleClientId}");
        url.Should().Contain("response_type=code");
        url.Should().Contain("access_type=offline"); // critical: required for refresh_token on Google
        url.Should().Contain("prompt=consent");
        url.Should().Contain("state=state-abc");
        // Scope must include openid + email + the IMAP scope.
        url.Should().Contain("openid");
        url.Should().Contain("email");
        url.Should().Contain("mail.google.com");
    }

    [Fact]
    public void BuildAuthorizeUrl_ContainsAllRequiredParams_Microsoft()
    {
        var url = MakeService().BuildAuthorizeUrl("microsoft", "state-xyz");

        url.Should().StartWith(ImapOAuthProvider.Microsoft.AuthorizeUrl);
        url.Should().Contain("IMAP.AccessAsUser.All");
        url.Should().Contain("offline_access");
    }

    [Fact]
    public void BuildAuthorizeUrl_ThrowsForUnknownProvider()
    {
        var act = () => MakeService().BuildAuthorizeUrl("yahoo", "state");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown OAuth provider*");
    }

    [Fact]
    public async Task ExchangeCodeForTokens_ReturnsParsedTokens_WithEmail()
    {
        var idToken = MakeIdToken(email: "user@example.com");
        var handler = StubHttpHandler.WithJson(new
        {
            access_token = "google-access-1",
            refresh_token = "google-refresh-1",
            expires_in = 3600,
            id_token = idToken,
            token_type = "Bearer",
        });
        var svc = MakeService(handler);

        var result = await svc.ExchangeCodeForTokensAsync("google", "auth-code", CancellationToken.None);

        result.AccessToken.Should().Be("google-access-1");
        result.RefreshToken.Should().Be("google-refresh-1");
        result.EmailAddress.Should().Be("user@example.com");
        result.AccessTokenExpiresAt.Should().Be(_clock.UtcNow.AddSeconds(3600));
        // Verify the POST body — must include grant_type, code, both creds, redirect_uri.
        handler.LastRequestBody.Should().Contain("grant_type=authorization_code");
        handler.LastRequestBody.Should().Contain("code=auth-code");
        handler.LastRequestBody.Should().Contain($"client_id={GoogleClientId}");
        handler.LastRequestBody.Should().Contain($"client_secret={GoogleSecret}");
    }

    [Fact]
    public async Task ExchangeCodeForTokens_RejectsResponseWithoutRefreshToken()
    {
        var handler = StubHttpHandler.WithJson(new
        {
            access_token = "x",
            id_token = MakeIdToken(email: "user@example.com"),
            expires_in = 3600,
        });

        var svc = MakeService(handler);
        var act = async () => await svc.ExchangeCodeForTokensAsync("google", "code", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no refresh token*");
    }

    [Fact]
    public async Task ExchangeCodeForTokens_RejectsResponseWithoutEmail()
    {
        // id_token without an email or preferred_username claim.
        var idToken = MakeIdToken(claims: new() { ["sub"] = "12345" });
        var handler = StubHttpHandler.WithJson(new
        {
            access_token = "x", refresh_token = "y",
            id_token = idToken, expires_in = 3600,
        });

        var svc = MakeService(handler);
        var act = async () => await svc.ExchangeCodeForTokensAsync("google", "code", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no email claim*");
    }

    [Fact]
    public async Task ExchangeCodeForTokens_PropagatesProvider4xxAsFriendlyMessage()
    {
        var handler = StubHttpHandler.WithStatus(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");
        var svc = MakeService(handler);

        var act = async () => await svc.ExchangeCodeForTokensAsync("google", "expired-code", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OAuth provider returned 400*");
    }

    [Fact]
    public async Task RefreshAccessToken_AcceptsResponseWithoutRotatedRefreshToken()
    {
        // Many providers omit refresh_token on refresh = "keep using yours".
        var handler = StubHttpHandler.WithJson(new
        {
            access_token = "google-access-2",
            expires_in = 3600,
        });
        var svc = MakeService(handler);

        var result = await svc.RefreshAccessTokenAsync("google", "old-refresh", CancellationToken.None);

        result.AccessToken.Should().Be("google-access-2");
        result.NewRefreshToken.Should().BeNull();
        result.AccessTokenExpiresAt.Should().Be(_clock.UtcNow.AddSeconds(3600));
        handler.LastRequestBody.Should().Contain("grant_type=refresh_token");
        handler.LastRequestBody.Should().Contain("refresh_token=old-refresh");
    }

    [Fact]
    public async Task RefreshAccessToken_PropagatesRotatedRefreshToken()
    {
        // Microsoft rotates; the new value must be persisted by the caller.
        var handler = StubHttpHandler.WithJson(new
        {
            access_token = "ms-access-2",
            refresh_token = "ms-refresh-2",
            expires_in = 3600,
        });
        var svc = MakeService(handler);

        var result = await svc.RefreshAccessTokenAsync("microsoft", "ms-refresh-1", CancellationToken.None);

        result.AccessToken.Should().Be("ms-access-2");
        result.NewRefreshToken.Should().Be("ms-refresh-2");
    }

    /// <summary>Manufactures a JWT id_token-like string with the given
    /// claims. Not signed (we don't validate signatures — only parse the
    /// payload).</summary>
    private static string MakeIdToken(string? email = null, Dictionary<string, string>? claims = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = "test",
            ["aud"] = "test",
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        };
        if (!string.IsNullOrEmpty(email)) payload["email"] = email;
        if (claims is not null) foreach (var (k, v) in claims) payload[k] = v;

        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var body = B64(JsonSerializer.Serialize(payload));
        return $"{header}.{body}.";
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }
        private HttpResponseMessage _response = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { }),
        };

        public static StubHttpHandler WithJson(object payload)
        {
            var h = new StubHttpHandler { _response = new(HttpStatusCode.OK) };
            h._response.Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            return h;
        }

        public static StubHttpHandler WithStatus(HttpStatusCode status, string body)
            => new() { _response = new(status) { Content = new StringContent(body) } };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return _response;
        }
    }
}
