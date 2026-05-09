using FluentAssertions;
using MailKit;
using MimeKit;

using QBEngineer.Api.Features.Communications;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Models.Communications;

namespace QBEngineer.Tests.Handlers.Communications;

/// <summary>
/// Wave 8 — IMAP adapter unit tests. Cover the pure-function pieces
/// (checkpoint parsing, mime-message translation) without standing up a
/// real IMAP server. End-to-end + Hangfire-driven IMAP integration is
/// validated manually against a real Gmail/Outlook account at the moment;
/// a future commit will wire MailKit's Greenmail-equivalent test harness.
/// </summary>
public class ImapEmailSyncProviderTests
{
    [Theory]
    [InlineData(null, null, 0u)]
    [InlineData("", null, 0u)]
    [InlineData("12345:67", 12345u, 67u)]
    [InlineData("999:0", 999u, 0u)]
    [InlineData("garbage", null, 0u)]
    [InlineData("1:2:3", null, 0u)]
    [InlineData("abc:def", null, 0u)]
    public void ParseCheckpoint_ReturnsExpected(string? raw, uint? expectedValidity, uint expectedUid)
    {
        var (validity, uid) = ImapEmailSyncProvider.ParseCheckpoint(raw);
        validity.Should().Be(expectedValidity);
        uid.Should().Be(expectedUid);
    }

    [Fact]
    public void TranslateMime_DetectsInbound_WhenSenderNotOwner()
    {
        var msg = new MimeMessage(
            from: new[] { new MailboxAddress("Customer", "customer@example.com") },
            to: new[] { new MailboxAddress("Sales Rep", "rep@us.test") },
            subject: "Pricing question",
            body: new TextPart("plain") { Text = "Body content" });
        msg.Date = new DateTimeOffset(2026, 5, 9, 10, 0, 0, TimeSpan.Zero);
        var uid = new UniqueId(1234, 56);

        var comm = ImapEmailSyncProvider.TranslateMimeMessage(msg, uid, "rep@us.test");

        comm.Direction.Should().Be(CommunicationDirection.Inbound);
        comm.From.Should().Be("customer@example.com");
        comm.To.Should().BeEquivalentTo(new[] { "rep@us.test" });
        comm.Subject.Should().Be("Pricing question");
        comm.ExternalId.Should().Be("imap-1234-56");
        comm.OccurredAt.Should().Be(msg.Date);
        comm.Kind.Should().Be(CommunicationKind.Email);
    }

    [Fact]
    public void TranslateMime_DetectsOutbound_WhenSenderIsOwner()
    {
        // Salesperson sent this — From matches the connected mailbox.
        // Direction routes to outbound so the matcher iterates To list
        // (could be a lead the rep is following up with).
        var msg = new MimeMessage(
            from: new[] { new MailboxAddress("Sales Rep", "rep@us.test") },
            to: new[] { new MailboxAddress("Lead", "lead@biz.com") },
            subject: "Following up",
            body: new TextPart("plain") { Text = "Body" });
        msg.Date = DateTimeOffset.UtcNow;
        var uid = new UniqueId(1234, 99);

        var comm = ImapEmailSyncProvider.TranslateMimeMessage(msg, uid, "rep@us.test");

        comm.Direction.Should().Be(CommunicationDirection.Outbound);
        comm.From.Should().Be("rep@us.test");
        comm.To.Should().BeEquivalentTo(new[] { "lead@biz.com" });
    }

    [Fact]
    public void TranslateMime_FlattensCcIntoToList()
    {
        // CC'd recipients still get matched. A salesperson CCing two leads
        // produces two matches; the matcher receives all addressed parties
        // in the To list (To + Cc combined).
        var msg = new MimeMessage(
            from: new[] { new MailboxAddress("Rep", "rep@us.test") },
            to: new[] { new MailboxAddress("A", "a@biz.com") },
            subject: "Joint follow-up",
            body: new TextPart("plain") { Text = "Body" });
        msg.Cc.Add(new MailboxAddress("B", "b@biz.com"));
        msg.Cc.Add(new MailboxAddress("C", "c@biz.com"));
        var uid = new UniqueId(1234, 100);

        var comm = ImapEmailSyncProvider.TranslateMimeMessage(msg, uid, "rep@us.test");

        comm.To.Should().BeEquivalentTo(new[] { "a@biz.com", "b@biz.com", "c@biz.com" });
    }

    [Fact]
    public void TranslateMime_TruncatesLongBody()
    {
        var bigBody = new string('x', 4000);
        var msg = new MimeMessage(
            from: new[] { new MailboxAddress("X", "x@test.com") },
            to: new[] { new MailboxAddress("Y", "y@test.com") },
            subject: "Big",
            body: new TextPart("plain") { Text = bigBody });
        var uid = new UniqueId(1234, 1);

        var comm = ImapEmailSyncProvider.TranslateMimeMessage(msg, uid, "y@test.com");

        comm.Body!.Length.Should().Be(2000);
    }

    [Fact]
    public void TranslateMime_HandlesEmptySubjectAndBody()
    {
        var msg = new MimeMessage(
            from: new[] { new MailboxAddress("X", "x@test.com") },
            to: new[] { new MailboxAddress("Y", "y@test.com") },
            subject: string.Empty,
            body: new TextPart("plain") { Text = string.Empty });
        var uid = new UniqueId(1234, 1);

        var comm = ImapEmailSyncProvider.TranslateMimeMessage(msg, uid, "y@test.com");

        comm.Subject.Should().NotBeNull();
        comm.Body.Should().BeNull();
    }
}
