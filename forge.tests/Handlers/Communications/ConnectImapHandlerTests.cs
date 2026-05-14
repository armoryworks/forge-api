using System.Text.Json;

using FluentAssertions;
using MailKit;
using MailKit.Security;
using MimeKit;

using Microsoft.AspNetCore.DataProtection;

using Forge.Api.Features.Communications;
using Forge.Core.Models.Communications;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Communications;

/// <summary>
/// Wave 8 phase 1h — IMAP connect handler tests. The handler test-
/// authenticates against the live server before persisting, so we drive
/// it with a fake <see cref="IImapClientFactory"/> that can be configured
/// to succeed, throw <see cref="AuthenticationException"/>, or throw a
/// network error. Password encryption uses the real
/// <see cref="EphemeralDataProtectionProvider"/> so the round-trip
/// through the sealed envelope is exercised end-to-end.
/// </summary>
public class ConnectImapHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly EphemeralDataProtectionProvider _dataProtection = new();

    public ConnectImapHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _db.CurrentUserId = 42;
    }

    private ConnectImapHandler MakeHandler(IImapClientFactory factory)
        => new(_db, _dataProtection, factory);

    [Fact]
    public async Task Connect_TestAuthenticatesThenPersists()
    {
        var factory = new StubImapClientFactory();
        var handler = MakeHandler(factory);

        var result = await handler.Handle(new ConnectImapCommand(
            Host: "imap.example.com", Port: 993, UseSsl: true,
            Username: "u@example.com", Password: "secret",
            Mailbox: null, DisplayLabel: "Work"), CancellationToken.None);

        result.IsConnected.Should().BeTrue();
        result.UserId.Should().Be(42);
        result.ProviderId.Should().Be("imap");
        result.ExternalAccountId.Should().Be("u@example.com");
        factory.Stub.ConnectCalls.Should().Be(1);
        factory.Stub.AuthCalls.Should().Be(1);

        // Verify the row landed with sealed envelope + correct ConfigJson.
        var saved = _db.CommunicationSyncConfigs.Single();
        saved.AccessToken.Should().NotBeNull().And.NotBe("secret"); // encrypted, not plaintext
        var config = JsonSerializer.Deserialize<ImapConnectionConfig>(saved.ConfigJson!);
        config!.Host.Should().Be("imap.example.com");
        config.Port.Should().Be(993);
        config.UseSsl.Should().BeTrue();
        config.Mailbox.Should().Be("INBOX"); // default applied
        config.Username.Should().Be("u@example.com");
    }

    [Fact]
    public async Task Connect_PasswordRoundTripsThroughDataProtection()
    {
        var factory = new StubImapClientFactory();
        var handler = MakeHandler(factory);

        await handler.Handle(new ConnectImapCommand(
            Host: "imap.example.com", Port: 993, UseSsl: true,
            Username: "u@example.com", Password: "p@ssw0rd-!",
            Mailbox: "INBOX", DisplayLabel: null), CancellationToken.None);

        var saved = _db.CommunicationSyncConfigs.Single();
        var unsealed = _dataProtection
            .CreateProtector("communication-sync.imap")
            .Unprotect(saved.AccessToken!);
        unsealed.Should().Be("p@ssw0rd-!");
    }

    [Fact]
    public async Task Connect_RejectsWrongPassword_WithFriendlyMessage()
    {
        var factory = new StubImapClientFactory(authThrows: new AuthenticationException("bad creds"));
        var handler = MakeHandler(factory);

        var act = async () => await handler.Handle(new ConnectImapCommand(
            Host: "imap.example.com", Port: 993, UseSsl: true,
            Username: "u@example.com", Password: "wrong",
            Mailbox: null, DisplayLabel: null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*username or password rejected*");

        // No row should land when the test-auth fails.
        _db.CommunicationSyncConfigs.Should().BeEmpty();
    }

    [Fact]
    public async Task Connect_RejectsUnreachableHost_WithFriendlyMessage()
    {
        var factory = new StubImapClientFactory(connectThrows: new System.Net.Sockets.SocketException(11001));
        var handler = MakeHandler(factory);

        var act = async () => await handler.Handle(new ConnectImapCommand(
            Host: "no-such-host.invalid", Port: 993, UseSsl: true,
            Username: "u@example.com", Password: "secret",
            Mailbox: null, DisplayLabel: null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unreachable*");
        _db.CommunicationSyncConfigs.Should().BeEmpty();
    }

    [Fact]
    public async Task Connect_RejectsDuplicate_SameUsername()
    {
        var factory = new StubImapClientFactory();
        var handler = MakeHandler(factory);

        await handler.Handle(new ConnectImapCommand(
            "imap.example.com", 993, true, "u@example.com", "secret", null, null),
            CancellationToken.None);

        var act = async () => await handler.Handle(new ConnectImapCommand(
            "imap.example.com", 993, true, "u@example.com", "secret", null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    private sealed class StubImapClientFactory(
        Exception? connectThrows = null,
        Exception? authThrows = null) : IImapClientFactory
    {
        public StubImapClient Stub { get; } = new(connectThrows, authThrows);

        public IImapClientWrapper Create() => Stub;
    }

    private sealed class StubImapClient(Exception? connectThrows, Exception? authThrows) : IImapClientWrapper
    {
        public bool IsConnected { get; private set; }
        public int ConnectCalls { get; private set; }
        public int AuthCalls { get; private set; }

        public Task ConnectAsync(string host, int port, bool useSsl, CancellationToken ct)
        {
            ConnectCalls++;
            if (connectThrows is not null) throw connectThrows;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task AuthenticateAsync(string username, string password, CancellationToken ct)
        {
            AuthCalls++;
            if (authThrows is not null) throw authThrows;
            return Task.CompletedTask;
        }

        public Task AuthenticateOAuthAsync(string username, string accessToken, CancellationToken ct)
            => Task.CompletedTask;

        public Task<IImapFolderWrapper> OpenFolderAsync(string mailbox, CancellationToken ct)
            => Task.FromResult<IImapFolderWrapper>(new StubFolder());

        public Task DisconnectAsync(bool quit, CancellationToken ct)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubFolder : IImapFolderWrapper
    {
        public uint UidValidity => 1;
        public Task<uint> GetHighestUidAsync(CancellationToken ct) => Task.FromResult(0u);
        public Task<IList<UniqueId>> SearchUidsAsync(MailKit.Search.SearchQuery query, CancellationToken ct)
            => Task.FromResult<IList<UniqueId>>(Array.Empty<UniqueId>());
        public Task<MimeMessage?> FetchMessageAsync(UniqueId uid, CancellationToken ct)
            => Task.FromResult<MimeMessage?>(null);
    }
}
