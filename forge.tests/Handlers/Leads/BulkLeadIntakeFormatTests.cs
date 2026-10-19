using FluentAssertions;
using Moq;

using Forge.Api.Features.Leads.BulkIntake;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Leads;

public class BulkLeadIntakeFormatTests
{
    private static BulkLeadIntakeRow Row(string key, string? email, string? phone) =>
        new(key, $"Company {key}", null, email, phone, null, null);

    [Fact]
    public async Task Rows_with_a_malformed_email_or_short_phone_are_skipped_with_a_reason()
    {
        using var db = TestDbContextFactory.Create();
        var clock = Mock.Of<IClock>(c => c.UtcNow == DateTimeOffset.UtcNow);
        var request = new BulkLeadIntakeRequest(BulkLeadIntakeStrategy.ManualEntry, null, null,
        [
            Row("bad-email", "not-an-email", null),
            Row("bad-phone", null, "12-34"),
            Row("good", "buyer@example.com", "(555) 010-2000"),
        ]);

        var result = await new BulkLeadIntakeHandler(db, clock)
            .Handle(new BulkLeadIntakeCommand(request, Commit: false), CancellationToken.None);

        result.Results.Single(r => r.ExternalRowKey == "bad-email").Status.Should().Be(BulkLeadIntakeRowStatus.Invalid);
        result.Results.Single(r => r.ExternalRowKey == "bad-email").Reason.Should().Contain("email");
        result.Results.Single(r => r.ExternalRowKey == "bad-phone").Status.Should().Be(BulkLeadIntakeRowStatus.Invalid);
        result.Results.Single(r => r.ExternalRowKey == "good").Status.Should().Be(BulkLeadIntakeRowStatus.Created);
    }
}
