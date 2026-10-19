using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Communications;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models.Communications;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Communications;

[Collection(PostgresCollection.Name)]
public sealed class CommunicationMatcherPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Outbound_mail_to_two_contacts_saves_under_the_channel_external_id_index_and_a_replay_is_a_no_op()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateContext();
        var customer = new Customer { Name = $"Globex {tag}" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        db.Contacts.AddRange(
            new Contact { CustomerId = customer.Id, FirstName = "A", LastName = "One", Email = $"a-{tag}@globex.test" },
            new Contact { CustomerId = customer.Id, FirstName = "B", LastName = "Two", Email = $"b-{tag}@globex.test" });
        await db.SaveChangesAsync();

        var mail = new InboundCommunication(
            ProviderId: "imap",
            Kind: CommunicationKind.Email,
            Direction: CommunicationDirection.Outbound,
            ExternalId: $"imap-{tag}-1",
            From: "rep@tenant.test",
            To: new[] { $"a-{tag}@globex.test", $"b-{tag}@globex.test" },
            OccurredAt: DateTimeOffset.UtcNow,
            Subject: "Revised quote",
            Body: "Body",
            DurationMinutes: null,
            RecordingUrl: null);

        var first = await new CommunicationMatcher(db).MatchAndLogAsync(mail, CancellationToken.None);
        await using var replayDb = fixture.CreateContext();
        var replay = await new CommunicationMatcher(replayDb).MatchAndLogAsync(mail, CancellationToken.None);

        await using var verify = fixture.CreateContext();
        var rows = await verify.Communications.AsNoTracking()
            .Where(c => c.Contact!.CustomerId == customer.Id).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Count(r => r.ExternalId == mail.ExternalId).Should().Be(1);
        first.InteractionIds.Should().HaveCount(2);
        replay.AlreadyIngested.Should().BeTrue();
    }
}
