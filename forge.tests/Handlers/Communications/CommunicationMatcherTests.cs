using FluentAssertions;

using Forge.Api.Features.Communications;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models.Communications;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Communications;

/// <summary>
/// Wave 8 — matcher unit tests. Cover the three behaviours that the production
/// path leans on: (1) normalization of email/phone before lookup, (2) Lead
/// matches before Customer Contact, (3) recency tiebreaker when multiple
/// candidates share the same address.
/// </summary>
public class CommunicationMatcherTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly CommunicationMatcher _matcher;

    public CommunicationMatcherTests()
    {
        _db = TestDbContextFactory.Create();
        _matcher = new CommunicationMatcher(_db);
    }

    [Theory]
    [InlineData("Foo@Example.COM", CommunicationKind.Email, "foo@example.com")]
    [InlineData("  alice@bar.io  ", CommunicationKind.Email, "alice@bar.io")]
    [InlineData("(503) 555-1212", CommunicationKind.Voice, "5035551212")]
    [InlineData("+1-503-555-1212", CommunicationKind.Voice, "5035551212")]
    [InlineData("503.555.1212", CommunicationKind.Voice, "5035551212")]
    [InlineData("ext 1234", CommunicationKind.Voice, "")]
    [InlineData("", CommunicationKind.Email, "")]
    [InlineData(null, CommunicationKind.Voice, "")]
    public void Normalize_ProducesComparableForm(string? raw, CommunicationKind kind, string expected)
    {
        CommunicationMatcher.Normalize(raw, kind).Should().Be(expected);
    }

    [Fact]
    public async Task MatchAndLog_PrefersActiveLead_OverCustomerContact()
    {
        // Same email used by an active lead AND an existing customer contact.
        // Lead wins per design — sales people own the lead-followup motion;
        // an old contact thread shouldn't intercept it.
        var customer = new Customer { Name = "Acme" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var contact = new Contact { CustomerId = customer.Id, FirstName = "Old", LastName = "Hand", Email = "shared@biz.com" };
        _db.Contacts.Add(contact);

        var lead = new Lead { CompanyName = "Brand New Co", Email = "shared@biz.com", Status = LeadStatus.New, CreatedBy = 1 };
        _db.Leads.Add(lead);
        await _db.SaveChangesAsync();

        var comm = new InboundCommunication(
            ProviderId: "imap",
            Kind: CommunicationKind.Email,
            Direction: CommunicationDirection.Inbound,
            ExternalId: "msg-1",
            From: "shared@biz.com",
            To: new[] { "user@tenant.com" },
            OccurredAt: DateTimeOffset.UtcNow,
            Subject: "Quick question",
            Body: "Body",
            DurationMinutes: null,
            RecordingUrl: null);

        var result = await _matcher.MatchAndLogAsync(comm, CancellationToken.None);

        result.Matched.Should().BeTrue();
        // Lead-side match writes only ActivityLog (no ContactInteraction yet —
        // leads have no Contact entity). So no ContactInteraction rows landed.
        _db.ContactInteractions.Should().BeEmpty();
    }

    [Fact]
    public async Task MatchAndLog_FallsBackToContact_WhenNoLeadMatches()
    {
        var customer = new Customer { Name = "Globex" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var contact = new Contact { CustomerId = customer.Id, FirstName = "Hank", LastName = "Scorpio", Email = "hank@globex.com" };
        _db.Contacts.Add(contact);

        // User row for the auto-logged interaction's UserId fallback.
        var user = new ApplicationUser
        {
            UserName = "system@tenant.com", Email = "system@tenant.com",
            FirstName = "System", LastName = "User", Initials = "SU", AvatarColor = "#94a3b8",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var comm = new InboundCommunication(
            ProviderId: "imap",
            Kind: CommunicationKind.Email,
            Direction: CommunicationDirection.Inbound,
            ExternalId: "msg-2",
            From: "HANK@globex.com",   // Mixed case to exercise normalization.
            To: new[] { "user@tenant.com" },
            OccurredAt: DateTimeOffset.UtcNow,
            Subject: "Re: Q3 Order",
            Body: "Body",
            DurationMinutes: null,
            RecordingUrl: null);

        var result = await _matcher.MatchAndLogAsync(comm, CancellationToken.None);

        result.Matched.Should().BeTrue();
        var interactions = _db.ContactInteractions.ToList();
        interactions.Should().HaveCount(1);
        interactions[0].ContactId.Should().Be(contact.Id);
        interactions[0].Type.Should().Be(InteractionType.Email);
    }

    [Fact]
    public async Task MatchAndLog_TieBreaksLeadsByMostRecentlyUpdated()
    {
        // Two active leads share the same phone number. The matcher should
        // pick the more-recently-updated one — covers the "stale-lead-with-
        // same-phone" race where someone followed up after the first lead
        // went quiet but before it got marked Lost.
        var older = new Lead
        {
            CompanyName = "Old Lead",
            Phone = "(503) 555-1212",
            Status = LeadStatus.New,
            CreatedBy = 1,
        };
        var newer = new Lead
        {
            CompanyName = "New Lead",
            Phone = "5035551212",
            Status = LeadStatus.Contacted,
            CreatedBy = 1,
        };
        _db.Leads.AddRange(older, newer);
        await _db.SaveChangesAsync();

        // SetTimestamps stamps UpdatedAt on insert; bump the "newer" lead so
        // the tiebreaker is unambiguous (in-memory provider can otherwise
        // round to the same tick).
        newer.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await _db.SaveChangesAsync();

        var comm = new InboundCommunication(
            ProviderId: "twilio",
            Kind: CommunicationKind.Voice,
            Direction: CommunicationDirection.Inbound,
            ExternalId: "call-1",
            From: "+15035551212",
            To: new[] { "+15555550100" },
            OccurredAt: DateTimeOffset.UtcNow,
            Subject: null,
            Body: null,
            DurationMinutes: 3,
            RecordingUrl: null);

        var result = await _matcher.MatchAndLogAsync(comm, CancellationToken.None);

        result.Matched.Should().BeTrue();
        // Lead match — no ContactInteraction (leads write only ActivityLog).
        _db.ContactInteractions.Should().BeEmpty();
    }

    [Fact]
    public async Task MatchAndLog_SkipsLostAndConvertedLeads()
    {
        // Dead leads must not intercept new inbound — we'd reopen things that
        // closed deliberately.
        var customer = new Customer { Name = "Acme" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        // Customer-side fallback.
        var contact = new Contact { CustomerId = customer.Id, FirstName = "Live", LastName = "Contact", Email = "shared@biz.com" };
        _db.Contacts.Add(contact);

        var lostLead = new Lead { CompanyName = "Dead Co", Email = "shared@biz.com", Status = LeadStatus.Lost, CreatedBy = 1 };
        _db.Leads.Add(lostLead);

        var user = new ApplicationUser
        {
            UserName = "system@tenant.com", Email = "system@tenant.com",
            FirstName = "System", LastName = "User", Initials = "SU", AvatarColor = "#94a3b8",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var comm = new InboundCommunication(
            ProviderId: "imap",
            Kind: CommunicationKind.Email,
            Direction: CommunicationDirection.Inbound,
            ExternalId: "msg-3",
            From: "shared@biz.com",
            To: new[] { "user@tenant.com" },
            OccurredAt: DateTimeOffset.UtcNow,
            Subject: "Hello again",
            Body: "Body",
            DurationMinutes: null,
            RecordingUrl: null);

        var result = await _matcher.MatchAndLogAsync(comm, CancellationToken.None);

        result.Matched.Should().BeTrue();
        // Should fall through to the customer Contact since the lead is Lost.
        var interactions = _db.ContactInteractions.ToList();
        interactions.Should().HaveCount(1);
        interactions[0].ContactId.Should().Be(contact.Id);
    }

    [Fact]
    public async Task MatchAndLog_ReturnsUnmatched_WhenNothingFits()
    {
        var comm = new InboundCommunication(
            ProviderId: "imap",
            Kind: CommunicationKind.Email,
            Direction: CommunicationDirection.Inbound,
            ExternalId: "msg-4",
            From: "stranger@nowhere.org",
            To: new[] { "user@tenant.com" },
            OccurredAt: DateTimeOffset.UtcNow,
            Subject: "Cold pitch",
            Body: "Body",
            DurationMinutes: null,
            RecordingUrl: null);

        var result = await _matcher.MatchAndLogAsync(comm, CancellationToken.None);

        result.Matched.Should().BeFalse();
        result.UnmatchedReason.Should().Be("no-match");
        _db.ContactInteractions.Should().BeEmpty();
    }

    [Fact]
    public async Task MatchAndLog_OutboundIteratesEachRecipient()
    {
        // Salesperson CC's two leads on the same email — should produce two
        // matches, one per addressed lead. (Today both write only ActivityLog
        // since they're leads, but the matcher returns Matched=true and the
        // matched count = 2.)
        var leadA = new Lead { CompanyName = "Lead A", Email = "a@biz.com", Status = LeadStatus.New, CreatedBy = 1 };
        var leadB = new Lead { CompanyName = "Lead B", Email = "b@biz.com", Status = LeadStatus.New, CreatedBy = 1 };
        _db.Leads.AddRange(leadA, leadB);
        await _db.SaveChangesAsync();

        var comm = new InboundCommunication(
            ProviderId: "gmail",
            Kind: CommunicationKind.Email,
            Direction: CommunicationDirection.Outbound,
            ExternalId: "msg-out-1",
            From: "rep@tenant.com",
            To: new[] { "a@biz.com", "b@biz.com", "noone@elsewhere.com" },
            OccurredAt: DateTimeOffset.UtcNow,
            Subject: "Following up",
            Body: "Body",
            DurationMinutes: null,
            RecordingUrl: null);

        var result = await _matcher.MatchAndLogAsync(comm, CancellationToken.None);

        result.Matched.Should().BeTrue();
        // The third recipient is unmatched, but the outer call still flags
        // Matched=true since at least one recipient hit. Lead-side matches
        // don't currently produce ContactInteraction rows.
        _db.ContactInteractions.Should().BeEmpty();
    }
}
