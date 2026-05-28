using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Remediation.Leads;

/// <summary>
/// TDD remediation tests for Region 1 · Leads audit findings.
/// RED-pending convention (see ../README.md): each test encodes the
/// definition-of-correct, is <c>[Fact(Skip="RED: …")]</c> so it compiles +
/// documents the contract without breaking the green gate. Burn-down = remove
/// the Skip + implement until it passes.
///
/// Findings covered here (api layer):
///   L3       (BLOCKER) — PullQueueHandler 500: Lead.Status stored int but raw SQL
///                        compares it to string literals ('Lost','Converted').
///   C1-back  (MED)     — UpdateLead permits an illegal status regression.
/// Not here (tracked in BACKLOG as UI/other):
///   L4 (campaign archive — OutreachCampaignsController, separate surface),
///   L7 (PM delete-button visibility — UI/Cypress).
///
/// Note: lead endpoints sit behind CAP-O2C-LEAD (default-on master data) and the
/// per-method [Authorize(Roles="Admin,Manager,PM")]; the integration test uses an
/// Admin client so it exercises the lifecycle guard, not the auth gate.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class LeadsRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;

    public LeadsRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    // L3 (Lead.Status persists as text) is proven against a real Postgres in
    // LeadQueuePullRemediationTests — both a model-converter assertion and the actual
    // queue-pull raw SQL. The InMemory provider doesn't expose GetValueConverter(), so the
    // assertion can't live here.

    [Fact(Skip = "RED: C1-back — UpdateLead permits an illegal status regression (Converted → New) " +
                 "with no state-machine guard in the handler/validator. Remove Skip when a backward " +
                 "lead-status transition is rejected (409).")]
    public async Task UpdateLead_rejects_status_regression_from_Converted_to_New()
    {
        int leadId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lead = new Lead { CompanyName = "C1-Regress", Status = LeadStatus.Converted };
            db.Leads.Add(lead);
            await db.SaveChangesAsync();
            leadId = lead.Id;
        }

        var body = JsonContent.Create(new { status = "New" });
        var response = await AuthClient("Admin").PatchAsync($"/api/v1/leads/{leadId}", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the lead funnel is forward-only — a Converted lead must not regress to New");
    }
}
