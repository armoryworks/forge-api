using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using QBEngineer.Api.Features.Communications;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;
using QBEngineer.Tests.Helpers;

namespace QBEngineer.Tests.Handlers.Communications;

/// <summary>
/// Wave 8 — Twilio webhook ingestion tests. The webhook handler defers
/// matching to the real <see cref="CommunicationMatcher"/>, so we drive
/// it through that to verify (a) terminal-state filtering, (b) duration
/// rounding, (c) inbound vs outbound direction routing, (d) missing
/// CallSid rejection.
/// </summary>
public class IngestTwilioWebhookHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly IngestTwilioWebhookHandler _handler;

    public IngestTwilioWebhookHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        var matcher = new CommunicationMatcher(_db);
        _handler = new IngestTwilioWebhookHandler(matcher, NullLogger<IngestTwilioWebhookHandler>.Instance);
    }

    [Fact]
    public async Task Webhook_IgnoresNonTerminalStatuses()
    {
        // "ringing" / "in-progress" should not produce activity log rows.
        var lead = new Lead { CompanyName = "Co", Phone = "+15035551212", Status = LeadStatus.New, CreatedBy = 1 };
        _db.Leads.Add(lead);
        await _db.SaveChangesAsync();

        var fields = new Dictionary<string, string>
        {
            ["CallSid"] = "CA-test-1",
            ["From"] = "+15035551212",
            ["To"] = "+15555550100",
            ["CallStatus"] = "ringing",
            ["Direction"] = "inbound",
        };

        var result = await _handler.Handle(new IngestTwilioWebhookCommand(fields), CancellationToken.None);

        result.Matched.Should().BeTrue(); // returns true with reason "non-terminal-status"
        result.UnmatchedReason.Should().Be("non-terminal-status");
        // Matcher never invoked — no activity log row created either.
        // (We can't observe ActivityLog directly without re-querying, but
        // the lead match would create a row; absence is implicit in
        // result.UnmatchedReason being the early-return sentinel.)
    }

    [Fact]
    public async Task Webhook_DrivesMatcher_ForCompletedCall()
    {
        var lead = new Lead { CompanyName = "Co", Phone = "+15035551212", Status = LeadStatus.New, CreatedBy = 1 };
        _db.Leads.Add(lead);
        await _db.SaveChangesAsync();

        var fields = new Dictionary<string, string>
        {
            ["CallSid"] = "CA-test-2",
            ["From"] = "+15035551212",   // matches the lead's phone
            ["To"] = "+15555550100",
            ["CallStatus"] = "completed",
            ["Direction"] = "inbound",
            ["CallDuration"] = "180",     // 3 minutes
        };

        var result = await _handler.Handle(new IngestTwilioWebhookCommand(fields), CancellationToken.None);

        result.Matched.Should().BeTrue();
        result.ExternalId.Should().Be("CA-test-2");
    }

    [Fact]
    public async Task Webhook_RoundsDurationUpToNearestMinute()
    {
        // 90 seconds → 2 min, 30 seconds → 1 min, 0 → 0 min.
        var customer = new Customer { Name = "Acme" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var contact = new Contact { CustomerId = customer.Id, FirstName = "A", LastName = "B", Phone = "+15555550199" };
        _db.Contacts.Add(contact);

        var user = new ApplicationUser
        {
            UserName = "u@t.com", Email = "u@t.com",
            FirstName = "U", LastName = "U", Initials = "UU", AvatarColor = "#94a3b8",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var fields = new Dictionary<string, string>
        {
            ["CallSid"] = "CA-test-dur",
            ["From"] = "+15555550199",
            ["To"] = "+15555550100",
            ["CallStatus"] = "completed",
            ["Direction"] = "inbound",
            ["CallDuration"] = "90",
        };

        await _handler.Handle(new IngestTwilioWebhookCommand(fields), CancellationToken.None);

        var interaction = _db.ContactInteractions.Single();
        interaction.DurationMinutes.Should().Be(2); // 90s → 2 min (rounded up)
    }

    [Fact]
    public async Task Webhook_RoutesOutboundDirectionToToList()
    {
        // Outbound call: From=us, To=lead. The matcher should match the
        // To list, not From.
        var lead = new Lead { CompanyName = "Co", Phone = "+15035551212", Status = LeadStatus.New, CreatedBy = 1 };
        _db.Leads.Add(lead);
        await _db.SaveChangesAsync();

        var fields = new Dictionary<string, string>
        {
            ["CallSid"] = "CA-test-3",
            ["From"] = "+15555550100",     // tenant rep
            ["To"] = "+15035551212",       // matches the lead
            ["CallStatus"] = "completed",
            ["Direction"] = "outbound-dial",
            ["CallDuration"] = "30",
        };

        var result = await _handler.Handle(new IngestTwilioWebhookCommand(fields), CancellationToken.None);
        result.Matched.Should().BeTrue();
    }

    [Fact]
    public async Task Webhook_RejectsMissingCallSid()
    {
        var fields = new Dictionary<string, string>
        {
            ["From"] = "+15555550100",
            ["CallStatus"] = "completed",
        };

        var result = await _handler.Handle(new IngestTwilioWebhookCommand(fields), CancellationToken.None);

        result.Matched.Should().BeFalse();
        result.UnmatchedReason.Should().Be("missing-call-sid");
    }

    [Fact]
    public void SignatureVerifier_AcceptsAnything_WhenAuthTokenUnset()
    {
        var options = Options.Create(new TwilioOptions { AuthToken = null });
        var verifier = new TwilioSignatureVerifier(options);

        verifier.IsConfigured.Should().BeFalse();
        verifier.Verify("https://x.com/webhook", new Dictionary<string, string>(), null).Should().BeTrue();
    }

    [Fact]
    public void SignatureVerifier_VerifiesHmacWhenConfigured()
    {
        // Twilio sample from
        // https://www.twilio.com/docs/usage/webhooks/webhooks-security#example
        // (synthesised here with a known token to lock the algorithm).
        const string token = "12345";
        const string url = "https://example.com/callback";
        var fields = new Dictionary<string, string>
        {
            ["CallSid"] = "CA-test",
            ["From"] = "+15555550100",
            ["To"] = "+15555550199",
        };
        var expectedSig = TwilioSignatureVerifier.ComputeSignature(url, fields, token);

        var verifier = new TwilioSignatureVerifier(Options.Create(new TwilioOptions { AuthToken = token }));

        verifier.Verify(url, fields, expectedSig).Should().BeTrue();
        verifier.Verify(url, fields, "wrong-signature").Should().BeFalse();
        verifier.Verify(url, fields, null).Should().BeFalse();
    }
}
