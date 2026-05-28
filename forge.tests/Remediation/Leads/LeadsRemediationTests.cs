using FluentAssertions;
using Moq;

using Forge.Api.Features.Leads;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Remediation.Leads;

/// <summary>
/// TDD remediation tests for Region 1 · Leads audit findings.
///   L3       (BLOCKER) — PullQueueHandler 500 (Lead.Status persisted int but the queue-pull
///                        raw SQL compares it to text). Proven against real Postgres in
///                        <see cref="LeadQueuePullRemediationTests"/>.
///   C1-back  (MED)     — UpdateLead permitted an illegal status regression out of Converted.
///
/// C1-back is exercised at the handler level: LeadsController pins AuthenticationSchemes to
/// JWT + SystemApiKey, so the InMemory WebApplicationFactory's test auth scheme can't reach
/// the endpoint (401). The guard lives in UpdateLeadHandler, so we drive it directly.
/// </summary>
public class LeadsRemediationTests
{
    [Fact] // C1-back — GREEN: Converted is terminal; a regression out of Converted throws (→409).
    public async Task UpdateLead_rejects_status_regression_out_of_Converted()
    {
        using var db = TestDbContextFactory.Create();
        var lead = new Lead { Id = 1, CompanyName = "C1-Regress", Status = LeadStatus.Converted };

        var repo = new Mock<ILeadRepository>();
        repo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(lead);

        var handler = new UpdateLeadHandler(repo.Object, db);
        var command = new UpdateLeadCommand(1,
            new UpdateLeadRequestModel(null, null, null, null, null, LeadStatus.New, null, null, null));

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "the lead funnel is forward-only out of Converted — a converted lead became a customer " +
            "(it carries ConvertedCustomerId); regressing it would orphan that link");
    }
}
