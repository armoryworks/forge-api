using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Leads.Queue;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Remediation.Leads;

/// <summary>
/// L3 (BLOCKER) — the worker-queue pull issues raw SQL comparing leads.status to text
/// literals ('Lost','Converted'). With Lead.Status persisted as int, Postgres rejected
/// "integer NOT IN (text)" → 500 on every pull. Now that Status persists as a string
/// (HasConversion&lt;string&gt; + the int→varchar migration), the comparison is valid.
/// Runs against a real Postgres because the failure is in raw SQL the InMemory provider
/// cannot execute (SqlQueryRaw / FOR UPDATE SKIP LOCKED).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LeadQueuePullRemediationTests(PostgresFixture fixture)
{
    [Fact] // L3 — Lead.Status maps to a text store column (so the queue-pull string compare is valid)
    public void Lead_Status_persists_as_a_text_column()
    {
        using var db = fixture.CreateContext();

        var prop = db.Model.FindEntityType(typeof(Lead))!.FindProperty(nameof(Lead.Status))!;
        var storeType = prop.GetRelationalTypeMapping().StoreType;

        storeType.Should().MatchRegex("character varying|varchar|text",
            "PullQueueHandler compares leads.status to text literals; Status must persist as a " +
            "text column, like every other Lead enum column — not the default integer");
    }

    [Fact]
    public async Task PullQueue_serves_a_queued_lead_without_a_text_comparison_500()
    {
        await using var db = fixture.CreateContext();
        await db.Leads.ExecuteDeleteAsync();

        var lead = new Lead
        {
            CompanyName = "L3-Queued",
            Status = LeadStatus.New,            // NOT IN (Lost, Converted) → eligible
            OutreachState = OutreachState.Queued,
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var handler = new PullQueueHandler(db, new SystemClock());

        var act = async () => await handler.Handle(
            new PullQueueCommand(1, new PullQueueRequest(null, 10)), default);

        var result = await act.Should().NotThrowAsync(
            "the queue-pull raw SQL must compare status as text, not 500 on integer = text");
        result.Subject.Should().ContainSingle().Which.Id.Should().Be(lead.Id);
    }
}
