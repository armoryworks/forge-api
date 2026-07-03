using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Watchtower;
using Forge.Api.Services;
using Forge.Core.Entities.Regulatory;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Watchtower;

/// <summary>
/// regulatory-watchtower (cluster B). The poller creates Pending proposals from a source's feed
/// (deduped), and review applies/dismisses with the reviewer recorded (propose-and-confirm).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RegulatoryWatchtowerTests(PostgresFixture fixture)
{
    private sealed class FakeFeedClient(IReadOnlyList<RegulatoryFeedItem> items) : IRegulatoryFeedClient
    {
        public Task<IReadOnlyList<RegulatoryFeedItem>> FetchAsync(RegulatorySource source, CancellationToken ct = default)
            => Task.FromResult(items);
    }

    [Fact]
    public async Task Poller_creates_pending_proposals_and_dedupes()
    {
        await using var db = fixture.CreateContext();
        await db.RegulatoryChangeProposals.ExecuteDeleteAsync();
        await db.RegulatorySources.ExecuteDeleteAsync();

        var src = new RegulatorySource { Name = "Federal Register", Url = "https://x", FeedType = RegulatoryFeedType.Api, IsActive = true };
        db.RegulatorySources.Add(src);
        await db.SaveChangesAsync();

        var items = new List<RegulatoryFeedItem> { new("New OSHA rule", "https://x/1", null) };

        (await new RegulatoryPoller(db, new FakeFeedClient(items), new SystemClock()).PollActiveAsync()).Should().Be(1);

        await using var db2 = fixture.CreateContext();
        (await new RegulatoryPoller(db2, new FakeFeedClient(items), new SystemClock()).PollActiveAsync())
            .Should().Be(0, "the same item is not proposed twice");

        await using var verify = fixture.CreateContext();
        (await verify.RegulatoryChangeProposals.CountAsync(p => p.Status == RegulatoryProposalStatus.Pending)).Should().Be(1);
    }

    [Fact]
    public async Task Review_applies_and_records_reviewer()
    {
        await using var db = fixture.CreateContext();
        await db.RegulatoryChangeProposals.ExecuteDeleteAsync();
        await db.RegulatorySources.ExecuteDeleteAsync();

        var src = new RegulatorySource { Name = "ATF", Url = "https://x", FeedType = RegulatoryFeedType.Scrape, IsActive = true };
        db.RegulatorySources.Add(src);
        await db.SaveChangesAsync();
        var proposal = new RegulatoryChangeProposal { RegulatorySourceId = src.Id, Title = "AFMER change", Status = RegulatoryProposalStatus.Pending };
        db.RegulatoryChangeProposals.Add(proposal);
        await db.SaveChangesAsync();

        db.CurrentUserId = 7;
        await new ReviewRegulatoryProposalHandler(db, new SystemClock())
            .Handle(new ReviewRegulatoryProposalCommand(proposal.Id, Apply: true), default);

        await using var verify = fixture.CreateContext();
        var reloaded = await verify.RegulatoryChangeProposals.SingleAsync(x => x.Id == proposal.Id);
        reloaded.Status.Should().Be(RegulatoryProposalStatus.Applied);
        reloaded.ReviewedByUserId.Should().Be(7);
        reloaded.ReviewedAt.Should().NotBeNull();
    }
}
