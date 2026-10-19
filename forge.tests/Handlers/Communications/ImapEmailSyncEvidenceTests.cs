using System.Text;
using System.Text.Json;

using FluentAssertions;
using MailKit;
using MailKit.Search;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;

using Forge.Api.Features.Communications;
using Forge.Api.Features.Communications.Extraction;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Core.Models.Communications;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Communications;

public class ImapEmailSyncEvidenceTests
{
    private const uint Validity = 7;
    private readonly Data.Context.AppDbContext _db = TestDbContextFactory.Create();
    private readonly EphemeralDataProtectionProvider _protection = new();

    private static MimeMessage Message(string from, string subject, byte[]? pdf = null)
    {
        var body = new BodyBuilder { TextBody = "Please quote 250 ea SA-210." };
        if (pdf is not null)
            body.Attachments.Add("rfq-210.pdf", pdf, new ContentType("application", "pdf"));
        var msg = new MimeMessage(
            from: new[] { new MailboxAddress("Sender", from) },
            to: new[] { new MailboxAddress("Sales", "sales@ourshop.test") },
            subject: subject,
            body: body.ToMessageBody());
        msg.Date = new DateTimeOffset(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);
        return msg;
    }

    private async Task<int> SeedConnectionAsync()
    {
        var config = new CommunicationSyncConfig
        {
            UserId = 1,
            Kind = CommunicationKind.Email,
            ProviderId = "imap",
            IsConnected = true,
            AccessToken = _protection.CreateProtector("communication-sync.imap").Protect("app-password"),
            ConfigJson = JsonSerializer.Serialize(new ImapConnectionConfig
            {
                Host = "imap.test", Username = "sales@ourshop.test", Mailbox = "INBOX",
            }),
            LastSyncedExternalId = $"{Validity}:10",
        };
        _db.CommunicationSyncConfigs.Add(config);
        await _db.SaveChangesAsync();
        return config.Id;
    }

    private ImapEmailSyncProvider Provider(IReadOnlyDictionary<uint, MimeMessage> mailbox)
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-10-07T15:00:00Z"));
        var resolver = new PartyResolver(_db, NullLogger<PartyResolver>.Instance);
        var artifacts = new ArtifactStore(
            _db, new MockStorageService(NullLogger<MockStorageService>.Instance),
            Options.Create(new MinioOptions { JobFilesBucket = "forge-files" }),
            clock, NullLogger<ArtifactStore>.Instance);
        var ingest = new IngestInboundEmailHandler(
            _db, resolver, artifacts,
            new HeuristicOrderExtractor(NullLogger<HeuristicOrderExtractor>.Instance),
            NullLogger<IngestInboundEmailHandler>.Instance);

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<IngestInboundEmailCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IngestInboundEmailCommand c, CancellationToken ct) => ingest.Handle(c, ct));

        return new ImapEmailSyncProvider(
            _db, new CommunicationMatcher(_db), resolver, mediator.Object, _protection,
            new FakeImapClientFactory(mailbox), Mock.Of<IImapOAuthService>(), clock,
            NullLogger<ImapEmailSyncProvider>.Instance);
    }

    [Fact]
    public async Task A_message_from_a_known_contact_keeps_its_attachment_and_a_stranger_is_not_stored()
    {
        var customer = new Customer { Name = "Brindle", IsActive = true };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        _db.Contacts.Add(new Contact { CustomerId = customer.Id, FirstName = "Nadia", LastName = "Buyer", Email = "nadia@brindle.test" });
        await _db.SaveChangesAsync();
        var connectionId = await SeedConnectionAsync();
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4 rfq");

        var provider = Provider(new Dictionary<uint, MimeMessage>
        {
            [11] = Message("nadia@brindle.test", "RFQ SA-210", pdf),
            [12] = Message("friend@personal.test", "Dinner Friday?"),
        });

        var matched = await provider.SyncRecentAsync(connectionId, CancellationToken.None);

        matched.Should().Be(1);
        var stored = _db.Communications.ToList();
        stored.Should().ContainSingle();
        stored[0].ExternalId.Should().Be($"imap-{connectionId}-{Validity}-11");
        stored[0].ContactId.Should().NotBeNull();
        var artifacts = _db.CommunicationArtifacts.Where(a => a.CommunicationId == stored[0].Id).ToList();
        artifacts.Count(a => a.Kind == CommunicationArtifactKind.Attachment).Should().Be(1);
        artifacts.Count(a => a.Kind == CommunicationArtifactKind.Message).Should().Be(1);
        _db.Communications.Any(c => c.FromAddress != null && c.FromAddress.Contains("personal.test")).Should().BeFalse();
        _db.CommunicationSyncConfigs.Single().LastSyncedExternalId.Should().Be($"{Validity}:12");
    }

    [Fact]
    public async Task The_same_uid_in_two_mailboxes_is_stored_once_per_mailbox()
    {
        var customer = new Customer { Name = "Brindle", IsActive = true };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        _db.Contacts.Add(new Contact { CustomerId = customer.Id, FirstName = "Nadia", LastName = "Buyer", Email = "nadia@brindle.test" });
        await _db.SaveChangesAsync();
        var first = await SeedConnectionAsync();
        var second = await SeedConnectionAsync();

        await Provider(new Dictionary<uint, MimeMessage> { [11] = Message("nadia@brindle.test", "RFQ to rep one") })
            .SyncRecentAsync(first, CancellationToken.None);
        var matched = await Provider(new Dictionary<uint, MimeMessage> { [11] = Message("nadia@brindle.test", "RFQ to rep two") })
            .SyncRecentAsync(second, CancellationToken.None);

        matched.Should().Be(1);
        _db.Communications.Select(c => c.Subject).ToList()
            .Should().BeEquivalentTo(new[] { "RFQ to rep one", "RFQ to rep two" });
    }

    [Fact]
    public void BuildInboundEmail_keeps_the_raw_message_and_decoded_attachments()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4 rfq");
        var msg = Message("nadia@brindle.test", "RFQ SA-210", pdf);
        var comm = ImapEmailSyncProvider.TranslateMimeMessage(msg, new UniqueId(Validity, 11), "sales@ourshop.test", 4);

        var email = ImapEmailSyncProvider.BuildInboundEmail(msg, comm);

        email.ExternalId.Should().Be($"imap-4-{Validity}-11");
        email.Attachments.Should().ContainSingle();
        email.Attachments[0].Filename.Should().Be("rfq-210.pdf");
        email.Attachments[0].ContentType.Should().Be("application/pdf");
        email.Attachments[0].Content.Should().Equal(pdf);
        Encoding.UTF8.GetString(email.RawMessage).Should().Contain("Subject: RFQ SA-210");
        email.ThreadId.Should().Be(msg.MessageId);
    }

    private sealed class FixedClock(DateTimeOffset now) : Core.Interfaces.IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeImapClientFactory(IReadOnlyDictionary<uint, MimeMessage> mailbox) : IImapClientFactory
    {
        public IImapClientWrapper Create() => new Client(mailbox);

        private sealed class Client(IReadOnlyDictionary<uint, MimeMessage> mailbox) : IImapClientWrapper
        {
            public bool IsConnected { get; private set; }
            public Task ConnectAsync(string host, int port, bool useSsl, CancellationToken ct) { IsConnected = true; return Task.CompletedTask; }
            public Task AuthenticateAsync(string username, string password, CancellationToken ct) => Task.CompletedTask;
            public Task AuthenticateOAuthAsync(string username, string accessToken, CancellationToken ct) => Task.CompletedTask;
            public Task<IImapFolderWrapper> OpenFolderAsync(string mailboxName, CancellationToken ct) => Task.FromResult<IImapFolderWrapper>(new Folder(mailbox));
            public Task DisconnectAsync(bool quit, CancellationToken ct) { IsConnected = false; return Task.CompletedTask; }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class Folder(IReadOnlyDictionary<uint, MimeMessage> mailbox) : IImapFolderWrapper
        {
            public uint UidValidity => Validity;
            public Task<uint> GetHighestUidAsync(CancellationToken ct) => Task.FromResult(mailbox.Keys.Max());
            public Task<IList<UniqueId>> SearchUidsAsync(SearchQuery query, CancellationToken ct)
                => Task.FromResult<IList<UniqueId>>(mailbox.Keys.OrderBy(k => k).Select(k => new UniqueId(Validity, k)).ToList());
            public Task<MimeMessage?> FetchMessageAsync(UniqueId uid, CancellationToken ct)
                => Task.FromResult(mailbox.TryGetValue(uid.Id, out var m) ? m : null);
        }
    }
}
