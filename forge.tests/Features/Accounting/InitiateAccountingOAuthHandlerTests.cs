using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Features.Accounting;
using Forge.Core.Models;

namespace Forge.Tests.Features.Accounting;

/// <summary>
/// Coverage for the InitiateAccountingOAuthCommand handler — Phase 2b.
/// Verifies that each supported provider builds a URL with the right
/// shape, persists state into session, and that unconfigured providers
/// surface a clear error instead of building a broken URL.
/// </summary>
public class InitiateAccountingOAuthHandlerTests
{
    private static (InitiateAccountingOAuthHandler handler, ISession session) MakeHandler(
        QuickBooksOptions? qb = null,
        XeroOptions? xero = null,
        FreshBooksOptions? fb = null,
        SageOptions? sage = null,
        ZohoOptions? zoho = null)
    {
        var session = new InMemorySession();
        var httpContext = new DefaultHttpContext { Session = session };
        var accessor = Mock.Of<IHttpContextAccessor>(a => a.HttpContext == httpContext);

        return (new InitiateAccountingOAuthHandler(
            accessor,
            Options.Create(qb ?? new QuickBooksOptions()),
            Options.Create(xero ?? new XeroOptions()),
            Options.Create(fb ?? new FreshBooksOptions()),
            Options.Create(sage ?? new SageOptions()),
            Options.Create(zoho ?? new ZohoOptions())),
            session);
    }

    [Fact]
    public async Task Handle_QuickBooks_Configured_ReturnsValidUrlWithState()
    {
        var (handler, session) = MakeHandler(qb: new QuickBooksOptions
        {
            ClientId = "qb-cid",
            ClientSecret = "qb-secret",
            RedirectUri = "https://forge.example/api/v1/quickbooks/callback",
        });

        var result = await handler.Handle(
            new InitiateAccountingOAuthCommand("quickbooks"), CancellationToken.None);

        result.Provider.Should().Be("quickbooks");
        result.AuthorizationUrl.Should().Contain("client_id=qb-cid");
        result.AuthorizationUrl.Should().Contain("response_type=code");
        result.AuthorizationUrl.Should().Contain($"state={result.State}");
        result.State.Should().NotBeNullOrEmpty();

        // State persisted to session for CSRF validation on callback.
        session.GetString("quickbooks_oauth_state").Should().Be(result.State);
    }

    [Fact]
    public async Task Handle_QuickBooks_NotConfigured_Throws()
    {
        var (handler, _) = MakeHandler(qb: new QuickBooksOptions { ClientId = "" });

        var act = () => handler.Handle(
            new InitiateAccountingOAuthCommand("quickbooks"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*QuickBooks is not configured*");
    }

    [Fact]
    public async Task Handle_Xero_BuildsCorrectUrl_AndStoresState()
    {
        var (handler, session) = MakeHandler(xero: new XeroOptions
        {
            ClientId = "xero-cid",
            ClientSecret = "xero-secret",
            RedirectUri = "https://forge.example/api/v1/xero/callback",
        });

        var result = await handler.Handle(
            new InitiateAccountingOAuthCommand("xero"), CancellationToken.None);

        result.AuthorizationUrl.Should().StartWith("https://login.xero.com/identity/connect/authorize");
        result.AuthorizationUrl.Should().Contain("client_id=xero-cid");
        session.GetString("xero_oauth_state").Should().Be(result.State);
    }

    [Fact]
    public async Task Handle_Sage_IncludesCountryCode()
    {
        var (handler, _) = MakeHandler(sage: new SageOptions
        {
            ClientId = "sage-cid",
            ClientSecret = "sage-secret",
            RedirectUri = "https://forge.example/api/v1/sage/callback",
            CountryCode = "GB",
        });

        var result = await handler.Handle(
            new InitiateAccountingOAuthCommand("sage"), CancellationToken.None);

        result.AuthorizationUrl.Should().Contain("country=GB");
    }

    [Fact]
    public async Task Handle_Zoho_IncludesOfflineAccessAndPromptConsent()
    {
        // Zoho's refresh-token issuance requires access_type=offline +
        // prompt=consent. The URL builder must include both or the
        // resulting connection has no refresh token (silently fails after
        // 1 hour).
        var (handler, _) = MakeHandler(zoho: new ZohoOptions
        {
            ClientId = "zoho-cid",
            ClientSecret = "zoho-secret",
            RedirectUri = "https://forge.example/api/v1/zoho/callback",
        });

        var result = await handler.Handle(
            new InitiateAccountingOAuthCommand("zoho"), CancellationToken.None);

        result.AuthorizationUrl.Should().Contain("access_type=offline");
        result.AuthorizationUrl.Should().Contain("prompt=consent");
    }

    [Fact]
    public async Task Handle_UnsupportedProvider_Throws()
    {
        var (handler, _) = MakeHandler();

        var act = () => handler.Handle(
            new InitiateAccountingOAuthCommand("wave"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not support OAuth*");
    }
}

/// <summary>
/// Minimal in-memory ISession for testing — just enough surface to back
/// SetString/GetString.
/// </summary>
internal sealed class InMemorySession : ISession
{
    private readonly Dictionary<string, byte[]> _data = new();

    public bool IsAvailable => true;
    public string Id => Guid.NewGuid().ToString();
    public IEnumerable<string> Keys => _data.Keys;

    public void Clear() => _data.Clear();
    public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Remove(string key) => _data.Remove(key);
    public void Set(string key, byte[] value) => _data[key] = value;
    public bool TryGetValue(string key, out byte[] value)
    {
        if (_data.TryGetValue(key, out var v)) { value = v; return true; }
        value = Array.Empty<byte>();
        return false;
    }
}
