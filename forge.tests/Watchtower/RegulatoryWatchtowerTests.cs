using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Watchtower;
using Forge.Api.Services;
using Forge.Core.Entities.Calendar;
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

    [Fact]
    public async Task Apply_with_due_date_creates_system_calendar_deadline()
    {
        await using var db = fixture.CreateContext();
        await db.Events.ExecuteDeleteAsync();
        await db.RegulatoryChangeProposals.ExecuteDeleteAsync();
        await db.RegulatorySources.ExecuteDeleteAsync();
        await db.CalendarEventTypes.ExecuteDeleteAsync();
        await db.CalendarSuperGroups.ExecuteDeleteAsync();

        var group = new CalendarSuperGroup { Key = "safety-osha", Name = "OSHA", RequiresTracking = true, SortOrder = 1 };
        db.CalendarSuperGroups.Add(group);
        await db.SaveChangesAsync();
        var type = new CalendarEventType { SuperGroupId = group.Id, Key = "osha-300a", Name = "OSHA 300A", RequiresTracking = true, SortOrder = 1 };
        db.CalendarEventTypes.Add(type);
        await db.SaveChangesAsync();
        var src = new RegulatorySource { Name = "OSHA", Url = "https://x", FeedType = RegulatoryFeedType.Rss, IsActive = true };
        db.RegulatorySources.Add(src);
        await db.SaveChangesAsync();
        var proposal = new RegulatoryChangeProposal { RegulatorySourceId = src.Id, Title = "New 300A deadline", Status = RegulatoryProposalStatus.Pending };
        db.RegulatoryChangeProposals.Add(proposal);
        await db.SaveChangesAsync();

        db.CurrentUserId = 1;
        var due = DateTimeOffset.UtcNow.AddDays(30);
        await new ReviewRegulatoryProposalHandler(db, new SystemClock())
            .Handle(new ReviewRegulatoryProposalCommand(proposal.Id, Apply: true, due, type.Id), default);

        await using var verify = fixture.CreateContext();
        var evt = await verify.Events.SingleAsync(e => e.EventTypeId == type.Id && e.IsSystemGenerated);
        evt.Title.Should().Be("New 300A deadline");
        evt.IsAllDay.Should().BeTrue();
    }
}
