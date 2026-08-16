using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Features.Communications;
using Forge.Core.Constants;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Communications;

/// <summary>
/// The confidence tiers, and the two rules that must hold no matter what is in
/// the database: a consumer mail domain can never be matched by wildcard, and an
/// address nothing claims is never guessed at.
/// </summary>
public class PartyResolverTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly PartyResolver _resolver;

    public PartyResolverTests()
    {
        _db = TestDbContextFactory.Create();
        _resolver = new PartyResolver(_db, NullLogger<PartyResolver>.Instance);
    }

    private Customer SeedCustomer(string name = "Bob's Parts")
    {
        var customer = new Customer { Name = name, IsActive = true };
        _db.Customers.Add(customer);
        _db.SaveChanges();
        return customer;
    }

    // ── Tier 1: exact contact address ──

    [Fact]
    public async Task ExactContactAddress_ResolvesExact()
    {
        var customer = SeedCustomer();
        _db.Contacts.Add(new Contact
        {
            CustomerId = customer.Id, FirstName = "Bob", LastName = "Vance", Email = "bob@bobsparts.com",
        });
        await _db.SaveChangesAsync();

        var result = await _resolver.ResolveAsync("bob@bobsparts.com", default);

        result.Confidence.Should().Be(CommunicationMatchConfidence.Exact);
        result.PartyType.Should().Be(CommunicationPartyType.Contact);
        result.ContactId.Should().NotBeNull();
        result.IsActionable.Should().BeTrue();
    }

    [Fact]
    public async Task ContactAddress_MatchesCaseInsensitivelyAndTrimmed()
    {
        var customer = SeedCustomer();
        _db.Contacts.Add(new Contact
        {
            CustomerId = customer.Id, FirstName = "Bob", LastName = "Vance", Email = "bob@bobsparts.com",
        });
        await _db.SaveChangesAsync();

        var result = await _resolver.ResolveAsync("  BOB@BobsParts.COM  ", default);

        result.Confidence.Should().Be(CommunicationMatchConfidence.Exact);
    }

    // ── Tier 3: domain rule ──

    [Fact]
    public async Task DomainRule_ResolvesDomainAndIsNotActionable()
    {
        var customer = SeedCustomer();
        _db.CommunicationIngestRules.Add(new CommunicationIngestRule
        {
            MatchType = IngestRuleMatchType.Domain,
            Pattern = "bobsparts.com",
            IsEnabled = true,
            PartyType = CommunicationPartyType.Customer,
            PartyId = customer.Id,
        });
        await _db.SaveChangesAsync();

        var result = await _resolver.ResolveAsync("someone-new@bobsparts.com", default);

        result.Confidence.Should().Be(CommunicationMatchConfidence.Domain);
        result.PartyId.Should().Be(customer.Id);

        // The load-bearing assertion: a domain match files the mail but must
        // never be enough to propose an order. Anyone at the domain can send.
        result.IsActionable.Should().BeFalse();
    }

    [Fact]
    public async Task DisabledRule_DoesNotMatch()
    {
        var customer = SeedCustomer();
        _db.CommunicationIngestRules.Add(new CommunicationIngestRule
        {
            MatchType = IngestRuleMatchType.Domain,
            Pattern = "bobsparts.com",
            IsEnabled = false,
            PartyType = CommunicationPartyType.Customer,
            PartyId = customer.Id,
        });
        await _db.SaveChangesAsync();

        var result = await _resolver.ResolveAsync("bob@bobsparts.com", default);

        result.Confidence.Should().Be(CommunicationMatchConfidence.Unmatched);
    }

    // ── The hard block ──

    [Theory]
    [InlineData("someone@gmail.com")]
    [InlineData("someone@yahoo.com")]
    [InlineData("someone@outlook.com")]
    [InlineData("someone@hotmail.com")]
    [InlineData("someone@icloud.com")]
    [InlineData("someone@proton.me")]
    [InlineData("someone@mailinator.com")]
    public async Task FreeMailDomain_IsNeverMatchedByWildcard_EvenWithAnEnabledRule(string address)
    {
        var customer = SeedCustomer();

        // A rule that should never exist. Bypassing the validator here is the
        // point: the resolver must refuse regardless of how the row got in —
        // direct INSERT, restored backup, or a row predating the guard.
        _db.CommunicationIngestRules.Add(new CommunicationIngestRule
        {
            MatchType = IngestRuleMatchType.Domain,
            Pattern = PartyResolver.DomainOf(address),
            IsEnabled = true,
            PartyType = CommunicationPartyType.Customer,
            PartyId = customer.Id,
        });
        await _db.SaveChangesAsync();

        var result = await _resolver.ResolveAsync(address, default);

        result.Confidence.Should().Be(CommunicationMatchConfidence.Unmatched);
        result.PartyId.Should().BeNull();
        result.Reason.Should().Contain("consumer mail provider");
    }

    [Fact]
    public async Task FreeMailAddress_StillResolvesWhenItIsAContactOnFile()
    {
        // The block is on the wildcard, not the provider. A sole trader whose
        // business address is Gmail is a legitimate exact match.
        var customer = SeedCustomer("Sole Trader");
        _db.Contacts.Add(new Contact
        {
            CustomerId = customer.Id, FirstName = "Sam", LastName = "Trader", Email = "sam@gmail.com",
        });
        await _db.SaveChangesAsync();

        var result = await _resolver.ResolveAsync("sam@gmail.com", default);

        result.Confidence.Should().Be(CommunicationMatchConfidence.Exact);
        result.IsActionable.Should().BeTrue();
    }

    [Fact]
    public async Task FreeMailAddress_ResolvesViaAnExplicitAddressRule()
    {
        var customer = SeedCustomer();
        _db.CommunicationIngestRules.Add(new CommunicationIngestRule
        {
            MatchType = IngestRuleMatchType.Address,
            Pattern = "sam@gmail.com",
            IsEnabled = true,
            PartyType = CommunicationPartyType.Customer,
            PartyId = customer.Id,
        });
        await _db.SaveChangesAsync();

        var result = await _resolver.ResolveAsync("sam@gmail.com", default);

        result.Confidence.Should().Be(CommunicationMatchConfidence.Exact);
        result.PartyId.Should().Be(customer.Id);
    }

    // ── Never guess ──

    [Fact]
    public async Task UnknownAddress_IsUnmatchedNotInferred()
    {
        var customer = SeedCustomer();
        _db.Contacts.Add(new Contact
        {
            CustomerId = customer.Id, FirstName = "Bob", LastName = "Vance", Email = "bob@bobsparts.com",
        });
        await _db.SaveChangesAsync();

        // Same domain as a known contact, with no domain rule. Inferring the
        // party from that shared domain would be a guess, and a guess here ends
        // up as evidence that someone authorized an order.
        var result = await _resolver.ResolveAsync("stranger@bobsparts.com", default);

        result.Confidence.Should().Be(CommunicationMatchConfidence.Unmatched);
        result.PartyId.Should().BeNull();
    }

    [Fact]
    public async Task RuleWithNoParty_LandsInTriageRatherThanMatching()
    {
        _db.CommunicationIngestRules.Add(new CommunicationIngestRule
        {
            MatchType = IngestRuleMatchType.Address,
            Pattern = "orders@bobsparts.com",
            IsEnabled = true,
            PartyId = null,
        });
        await _db.SaveChangesAsync();

        var result = await _resolver.ResolveAsync("orders@bobsparts.com", default);

        result.Confidence.Should().Be(CommunicationMatchConfidence.Unmatched);
        result.Reason.Should().Contain("triage");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    public async Task Unparseable_IsUnmatched(string address)
    {
        var result = await _resolver.ResolveAsync(address, default);
        result.Confidence.Should().Be(CommunicationMatchConfidence.Unmatched);
    }

    // ── The blocklist contract ──

    [Theory]
    [InlineData("gmail.com", true)]
    [InlineData("GMAIL.COM", true)]
    [InlineData("someone@gmail.com", true)]
    [InlineData("gmail.com.", true)]
    [InlineData("bobsparts.com", false)]
    [InlineData("notgmail.com", false)]
    [InlineData("gmail.com.evil.co", false)]
    [InlineData(null, false)]
    public void FreeMailDomains_RecognisesConsumerProviders(string? input, bool expected)
    {
        FreeMailDomains.IsFreeMail(input).Should().Be(expected);
    }
}
