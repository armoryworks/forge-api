using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Capabilities.Discovery;
using Forge.Data.Context;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Phase 4 Phase-F — Tests for the discovery flow:
///   • Question catalog (self-serve vs consultant mode)
///   • Recommendation engine (branch routing, override, alternatives)
///   • Preview endpoint (no persistence)
///   • Apply endpoint (persists DiscoveryRun + applies deltas + audit)
///   • Auth (admin-only)
///
/// Integration-shaped tests use HTTP. Pure-engine tests are unit tests
/// against <see cref="DiscoveryRecommendationEngine"/> directly.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class DiscoveryFlowTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;

    public DiscoveryFlowTests(CapabilityTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AuthenticatedClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    // ─── Question catalog endpoint ─────────────────────────────────────────

    [Fact]
    public async Task Questions_SelfServe_Returns_All_Branches()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/api/v1/discovery/questions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<QuestionsResponseRow>();
        Assert.NotNull(result);
        // The catalog ships 28 self-serve questions: 1 top-of-funnel (Q-S1,
        // Pro Services rollout D4) + 6 opening + 4 per branch × 3 + 2 override
        // + 6 diagnostic + 1 exit. A given user typically answers fewer because
        // only one branch applies AND Q-S1 = "services" / "both" short-circuits
        // the entire mfg tree — the wizard filters at render time.
        Assert.Equal(28, result!.Questions.Count);
        Assert.Equal(28, result.SelfServeCount);

        // Verify the opening / branch / override / diagnostic / exit categories
        // are all present.
        Assert.Contains(result.Questions, q => q.Id == "Q-O1");
        Assert.Contains(result.Questions, q => q.Id == "Q-A1");
        Assert.Contains(result.Questions, q => q.Id == "Q-B1");
        Assert.Contains(result.Questions, q => q.Id == "Q-C1");
        Assert.Contains(result.Questions, q => q.Id == "Q-V1");
        Assert.Contains(result.Questions, q => q.Id == "Q-D1");
        Assert.Contains(result.Questions, q => q.Id == "Q-X1");
    }

    [Fact]
    public async Task Questions_Consultant_Adds_Deepdive_Questions()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/api/v1/discovery/questions?mode=consultant");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<QuestionsResponseRow>();
        Assert.NotNull(result);
        Assert.True(result!.Questions.Count > 22);
        // 4-6 deepdive per branch × 3 branches expected — at least 12.
        Assert.True(result.ConsultantDeepdiveCount >= 12);
        Assert.Contains(result.Questions, q => q.Id == "Q-A5");
        Assert.Contains(result.Questions, q => q.Id == "Q-B5");
        Assert.Contains(result.Questions, q => q.Id == "Q-C5");
    }

    [Fact]
    public async Task Questions_Requires_Admin_Role()
    {
        var nonAdmin = AuthenticatedClient(role: "Engineer");
        var response = await nonAdmin.GetAsync("/api/v1/discovery/questions");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─── Preview endpoint (Branch A — small shop, not regulated) ──────────

    [Fact]
    public async Task Preview_BranchA_SmallShop_Maps_To_TwoPersonShop_Or_GrowingJobShop()
    {
        var client = AuthenticatedClient();
        var body = new
        {
            answers = new[]
            {
                new { questionId = "Q-O1", value = "1-2" },
                new { questionId = "Q-O3", value = "make" },
                new { questionId = "Q-O4", value = "no" },
                new { questionId = "Q-O5", value = "1" },
                new { questionId = "Q-A1", value = "none" },
                new { questionId = "Q-A2", value = "same-person" },
                new { questionId = "Q-A3", value = "single-step" },
            },
        };
        var response = await client.PostAsJsonAsync("/api/v1/discovery/preview", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseRow>();
        Assert.NotNull(result);
        Assert.Equal("PRESET-01", result!.PresetId);
        Assert.Equal("high", result.ConfidenceLabel);
    }

    [Fact]
    public async Task Preview_BranchB_Regulated_Override_Routes_To_Regulated_Manufacturer()
    {
        var client = AuthenticatedClient();
        var body = new
        {
            answers = new[]
            {
                new { questionId = "Q-O1", value = "26-50" },
                new { questionId = "Q-O3", value = "make" },
                new { questionId = "Q-O4", value = "medical" },
                new { questionId = "Q-O5", value = "1" },
                new { questionId = "Q-B1", value = "formal" },
                new { questionId = "Q-B2", value = "formal-ncr" },
            },
        };
        var response = await client.PostAsJsonAsync("/api/v1/discovery/preview", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseRow>();
        Assert.NotNull(result);
        Assert.Equal("PRESET-05", result!.PresetId);
        Assert.Contains(result.Factors, f => f.QuestionId == "Q-O4");
    }

    [Fact]
    public async Task Preview_BranchC_MultiSite_Routes_To_MultiSite_Operation()
    {
        var client = AuthenticatedClient();
        var body = new
        {
            answers = new[]
            {
                new { questionId = "Q-O1", value = "51-200" },
                new { questionId = "Q-O3", value = "make" },
                new { questionId = "Q-O4", value = "no" },
                new { questionId = "Q-O5", value = "2" },
                new { questionId = "Q-C1", value = "weekly" },
                new { questionId = "Q-C2", value = "fixed" },
                new { questionId = "Q-C3", value = "no" },
                new { questionId = "Q-C4", value = "no" },
            },
        };
        var response = await client.PostAsJsonAsync("/api/v1/discovery/preview", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseRow>();
        Assert.NotNull(result);
        Assert.Equal("PRESET-06", result!.PresetId);
    }

    [Fact]
    public async Task Preview_4C_Decision10_FiftyToOneHundred_TwoSites_Wins_MultiSite()
    {
        // Per 4C decision #10: 50-100 headcount + 2 sites should pick
        // PRESET-06 (Multi-Site), not PRESET-04. The 2-site marker dominates.
        var client = AuthenticatedClient();
        var body = new
        {
            answers = new[]
            {
                new { questionId = "Q-O1", value = "51-200" },
                new { questionId = "Q-O3", value = "make" },
                new { questionId = "Q-O4", value = "no" },
                new { questionId = "Q-O5", value = "2" },
                new { questionId = "Q-C1", value = "monthly" }, // even with marginal transfers
                new { questionId = "Q-B1", value = "formal" }, // would otherwise pull to PRESET-04
            },
        };
        var response = await client.PostAsJsonAsync("/api/v1/discovery/preview", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseRow>();
        Assert.NotNull(result);
        Assert.Equal("PRESET-06", result!.PresetId);
    }

    [Fact]
    public async Task Preview_LowConfidence_Surfaces_Alternatives()
    {
        // Boundary headcount + branch B with split-direction signals = low confidence.
        var client = AuthenticatedClient();
        var body = new
        {
            answers = new[]
            {
                new { questionId = "Q-O1", value = "26-50" },
                new { questionId = "Q-O3", value = "make" },
                new { questionId = "Q-O4", value = "no" },
                new { questionId = "Q-O5", value = "1" },
                new { questionId = "Q-B1", value = "no" },
                new { questionId = "Q-B2", value = "capa-loop" },
                new { questionId = "Q-V2", value = "We have a very unusual setup with a captive customer relationship that drives most of our revenue, plus a small custom-build program for boutique clients." },
            },
        };
        var response = await client.PostAsJsonAsync("/api/v1/discovery/preview", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseRow>();
        Assert.NotNull(result);
        // Alternatives exist when confidence is below threshold (0.7).
        Assert.True(result!.Confidence < 0.7);
        Assert.NotEmpty(result.Alternatives);
    }

    [Fact]
    public async Task Preview_Distribution_Mode_Routes_To_PRESET03()
    {
        var client = AuthenticatedClient();
        var body = new
        {
            answers = new[]
            {
                new { questionId = "Q-O1", value = "11-25" },
                new { questionId = "Q-O3", value = "resell" },
                new { questionId = "Q-O4", value = "no" },
                new { questionId = "Q-O5", value = "1" },
            },
        };
        var response = await client.PostAsJsonAsync("/api/v1/discovery/preview", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseRow>();
        Assert.NotNull(result);
        Assert.Equal("PRESET-03", result!.PresetId);
    }

    [Fact]
    public async Task Preview_QO3_ServicesOnly_Routes_To_PRESET08_Even_When_QS1_Said_Products()
    {
        // Q-O3 became multi-choice (services / make / resell). When the
        // user checks ONLY services at Q-O3, that overrides whatever
        // Q-S1 said — they effectively changed their mind. PRESET-08
        // (Pro Services) is the right reorientation.
        var client = AuthenticatedClient();
        var body = new
        {
            answers = new[]
            {
                new { questionId = "Q-S1", value = "products" },
                new { questionId = "Q-O1", value = "3-10" },
                new { questionId = "Q-O3", value = "services" },
                new { questionId = "Q-O4", value = "no" },
                new { questionId = "Q-O5", value = "1" },
            },
        };
        var response = await client.PostAsJsonAsync("/api/v1/discovery/preview", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseRow>();
        Assert.NotNull(result);
        Assert.Equal("PRESET-08", result!.PresetId);
    }

    [Fact]
    public async Task Preview_QO3_ServicesAlongsideMake_FallsThrough_To_Manufacturing_Flow()
    {
        // The other half of the contract: services CHECKED ALONGSIDE
        // make/resell is just "we sell some services as line items"
        // and must NOT reorient to PRESET-08. The manufacturing/distribution
        // recommendation engine handles it normally; services becomes a
        // line-item type via existing reference data.
        var client = AuthenticatedClient();
        var body = new
        {
            answers = new[]
            {
                new { questionId = "Q-S1", value = "products" },
                new { questionId = "Q-O1", value = "3-10" },
                new { questionId = "Q-O3", value = "services,make" },
                new { questionId = "Q-O4", value = "no" },
                new { questionId = "Q-O5", value = "1" },
            },
        };
        var response = await client.PostAsJsonAsync("/api/v1/discovery/preview", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseRow>();
        Assert.NotNull(result);
        // Must NOT be PRESET-08 — services-as-line-item doesn't reorient.
        Assert.NotEqual("PRESET-08", result!.PresetId);
        // Make-only mode at this headcount lands on a manufacturing preset
        // (PRESET-01 or PRESET-02 depending on the branch routing).
        Assert.Contains(result.PresetId, new[] { "PRESET-01", "PRESET-02" });
    }

    [Fact]
    public async Task Preview_QO3_MultiChoice_ResellPlusMake_Routes_Same_As_Legacy_Both()
    {
        // Backward-compat: pre-multi-select Q-O3 used single value "both"
        // for make+resell. Multi-choice now sends "make,resell" — must
        // resolve to the same preset.
        var client = AuthenticatedClient();
        var body = new
        {
            answers = new[]
            {
                new { questionId = "Q-O1", value = "11-25" },
                new { questionId = "Q-O3", value = "make,resell" },
                new { questionId = "Q-O4", value = "no" },
                new { questionId = "Q-O5", value = "1" },
            },
        };
        var response = await client.PostAsJsonAsync("/api/v1/discovery/preview", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseRow>();
        Assert.NotNull(result);
        // hybrid mode at this headcount lands on a production manufacturer
        // family preset (the exact preset depends on the engine's branch
        // routing; key contract is "not distribution, not services").
        Assert.NotEqual("PRESET-03", result!.PresetId);
        Assert.NotEqual("PRESET-08", result.PresetId);
    }

    [Fact]
    public async Task Preview_Returns_Capability_Deltas()
    {
        // PRESET-04 vs an out-of-the-box default install should produce a
        // non-empty delta list.
        var client = AuthenticatedClient();
        var body = new
        {
            answers = new[]
            {
                new { questionId = "Q-O1", value = "51-200" },
                new { questionId = "Q-O3", value = "make" },
                new { questionId = "Q-O4", value = "no" },
                new { questionId = "Q-O5", value = "1" },
                new { questionId = "Q-B1", value = "formal" },
                new { questionId = "Q-B2", value = "formal-ncr" },
                new { questionId = "Q-B3", value = "yes" },
                new { questionId = "Q-B4", value = "yes" },
            },
        };
        var response = await client.PostAsJsonAsync("/api/v1/discovery/preview", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseRow>();
        Assert.NotNull(result);
        Assert.NotEmpty(result!.CapabilityDeltas);
        // QC inspection / NCR / CAPA must be in the deltas as enable-actions.
        Assert.Contains(result.CapabilityDeltas, d => d.Code == "CAP-QC-INSPECTION" && d.WillBeEnabled);
    }

    // ─── Apply endpoint ────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_Persists_DiscoveryRun_And_Writes_Audit()
    {
        var client = AuthenticatedClient();

        // Reset to a known baseline by applying PRESET-01 first (mostly defaults).
        var body = new
        {
            answers = new[]
            {
                new { questionId = "Q-O1", value = "1-2" },
                new { questionId = "Q-O3", value = "make" },
                new { questionId = "Q-O4", value = "no" },
                new { questionId = "Q-O5", value = "1" },
                new { questionId = "Q-A1", value = "none" },
                new { questionId = "Q-A2", value = "same-person" },
                new { questionId = "Q-A3", value = "single-step" },
            },
            chosenPresetId = "PRESET-01",
            consultantMode = false,
        };
        var response = await client.PostAsJsonAsync("/api/v1/discovery/apply", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseRow>();
        Assert.NotNull(result);
        Assert.Equal("PRESET-01", result!.PresetId);

        // Verify a DiscoveryRun row was written.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.DiscoveryRuns
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(run);
        Assert.Equal("PRESET-01", run!.AppliedPresetId);
        Assert.False(run.RanInConsultantMode);
        Assert.Contains("Q-O1", run.AnswersJson);

        // Verify a DiscoveryApplied audit row exists.
        var auditRow = await db.AuditLogEntries
            .Where(a => a.Action == "DiscoveryApplied")
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(auditRow);
    }

    [Fact]
    public async Task Apply_Rerun_Overwrites_State_And_Adds_New_Run_Row()
    {
        var client = AuthenticatedClient();

        // Run discovery twice with different chosen presets.
        var run1Body = new
        {
            answers = new[]
            {
                new { questionId = "Q-O1", value = "11-25" },
                new { questionId = "Q-O3", value = "make" },
                new { questionId = "Q-O4", value = "no" },
                new { questionId = "Q-O5", value = "1" },
                new { questionId = "Q-A1", value = "quickbooks" },
                new { questionId = "Q-A2", value = "split-roles" },
                new { questionId = "Q-A3", value = "two-three" },
            },
            chosenPresetId = "PRESET-02",
            consultantMode = false,
        };
        var r1 = await client.PostAsJsonAsync("/api/v1/discovery/apply", run1Body);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        using var scope1 = _factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
        var initialRunCount = await db1.DiscoveryRuns.CountAsync();

        // Re-run with a different preset.
        var run2Body = new
        {
            answers = new[]
            {
                new { questionId = "Q-O1", value = "1-2" },
                new { questionId = "Q-O3", value = "make" },
                new { questionId = "Q-O4", value = "no" },
                new { questionId = "Q-O5", value = "1" },
                new { questionId = "Q-A1", value = "none" },
                new { questionId = "Q-A2", value = "same-person" },
                new { questionId = "Q-A3", value = "single-step" },
            },
            chosenPresetId = "PRESET-01",
            consultantMode = false,
        };
        var r2 = await client.PostAsJsonAsync("/api/v1/discovery/apply", run2Body);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        // A second DiscoveryRun row exists. The previous one is preserved.
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var afterRunCount = await db2.DiscoveryRuns.CountAsync();
        Assert.Equal(initialRunCount + 1, afterRunCount);
    }

    [Fact]
    public async Task Apply_Requires_Admin_Role()
    {
        var nonAdmin = AuthenticatedClient(role: "Engineer");
        var body = new
        {
            answers = new[] { new { questionId = "Q-O1", value = "1-2" } },
            chosenPresetId = "PRESET-01",
            consultantMode = false,
        };
        var response = await nonAdmin.PostAsJsonAsync("/api/v1/discovery/apply", body);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Preview_Requires_Admin_Role()
    {
        var nonAdmin = AuthenticatedClient(role: "Engineer");
        var body = new
        {
            answers = new[] { new { questionId = "Q-O1", value = "1-2" } },
        };
        var response = await nonAdmin.PostAsJsonAsync("/api/v1/discovery/preview", body);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─── Engine unit tests ─────────────────────────────────────────────────

    [Fact]
    public void Engine_Recommends_Custom_When_QX1_Yes()
    {
        var answers = new DiscoveryAnswerSet(
        [
            new DiscoveryAnswer("Q-O1", "11-25"),
            new DiscoveryAnswer("Q-O3", "make"),
            new DiscoveryAnswer("Q-X1", "yes"),
        ]);
        var rec = DiscoveryRecommendationEngine.Recommend(answers);
        Assert.Equal("PRESET-CUSTOM", rec.PresetId);
        Assert.Equal("high", rec.ConfidenceLabel);
    }

    [Fact]
    public void Engine_Routes_Branch_C_When_Multi_Site_Even_At_Mid_Headcount()
    {
        // Per 4C decision #4: multi-site = yes always routes to Branch C.
        var answers = new DiscoveryAnswerSet(
        [
            new DiscoveryAnswer("Q-O1", "26-50"),
            new DiscoveryAnswer("Q-O3", "make"),
            new DiscoveryAnswer("Q-O4", "no"),
            new DiscoveryAnswer("Q-O5", "2"),
            new DiscoveryAnswer("Q-C1", "weekly"),
        ]);
        var rec = DiscoveryRecommendationEngine.Recommend(answers);
        Assert.Equal("PRESET-06", rec.PresetId);
    }

    [Fact]
    public void Engine_Soft_Regulation_Override_From_Two_Signals()
    {
        // Q-O4 = no but Q-D1 + Q-V1 both fire → override to PRESET-05.
        var answers = new DiscoveryAnswerSet(
        [
            new DiscoveryAnswer("Q-O1", "11-25"),
            new DiscoveryAnswer("Q-O3", "make"),
            new DiscoveryAnswer("Q-O4", "no"),
            new DiscoveryAnswer("Q-O5", "1"),
            new DiscoveryAnswer("Q-A1", "quickbooks"),
            new DiscoveryAnswer("Q-A2", "split-roles"),
            new DiscoveryAnswer("Q-A3", "two-three"),
            new DiscoveryAnswer("Q-D1", "lots"),
            new DiscoveryAnswer("Q-V1", "Our biggest customer requires lot trace on every shipment and audits us yearly on our processes."),
        ]);
        var rec = DiscoveryRecommendationEngine.Recommend(answers);
        Assert.Equal("PRESET-05", rec.PresetId);
    }

    // ─── D4 — top-of-funnel products/services/both short-circuit ─────────

    [Fact]
    public void Engine_QS1_Services_Routes_Directly_To_Preset08()
    {
        var answers = new DiscoveryAnswerSet(new[]
        {
            new DiscoveryAnswer("Q-S1", "services"),
            // Even though the rest of the answers describe a small mfg shop,
            // Q-S1 = services short-circuits BEFORE the branch logic runs.
            new DiscoveryAnswer("Q-O1", "3-10"),
            new DiscoveryAnswer("Q-O3", "make"),
            new DiscoveryAnswer("Q-O4", "no"),
        });
        var rec = DiscoveryRecommendationEngine.Recommend(answers);
        Assert.Equal("PRESET-08", rec.PresetId);
        Assert.Equal("high", rec.ConfidenceLabel);
        Assert.Contains(rec.Factors, f => f.QuestionId == "Q-S1");
        Assert.Contains(rec.Alternatives, a => a.PresetId == "PRESET-09");
    }

    [Fact]
    public void Engine_QS1_Both_Routes_Directly_To_Preset09()
    {
        var answers = new DiscoveryAnswerSet(new[]
        {
            new DiscoveryAnswer("Q-S1", "both"),
            new DiscoveryAnswer("Q-O1", "26-50"),
        });
        var rec = DiscoveryRecommendationEngine.Recommend(answers);
        Assert.Equal("PRESET-09", rec.PresetId);
        Assert.Equal("high", rec.ConfidenceLabel);
        Assert.Contains(rec.Alternatives, a => a.PresetId == "PRESET-08");
        Assert.Contains(rec.Alternatives, a => a.PresetId == "PRESET-04");
    }

    [Fact]
    public void Engine_QS1_Products_Falls_Through_To_Existing_Logic()
    {
        // Backward compatibility — Q-S1 = products is the implied default.
        // The recommendation should match what we'd get without Q-S1 at all.
        var withQS1 = new DiscoveryAnswerSet(new[]
        {
            new DiscoveryAnswer("Q-S1", "products"),
            new DiscoveryAnswer("Q-O1", "1-2"),
            new DiscoveryAnswer("Q-O3", "make"),
            new DiscoveryAnswer("Q-A2", "same-person"),
        });
        var withoutQS1 = new DiscoveryAnswerSet(new[]
        {
            new DiscoveryAnswer("Q-O1", "1-2"),
            new DiscoveryAnswer("Q-O3", "make"),
            new DiscoveryAnswer("Q-A2", "same-person"),
        });

        var recWith = DiscoveryRecommendationEngine.Recommend(withQS1);
        var recWithout = DiscoveryRecommendationEngine.Recommend(withoutQS1);
        Assert.Equal(recWithout.PresetId, recWith.PresetId);
        Assert.Equal("PRESET-01", recWith.PresetId);
    }

    [Fact]
    public void Engine_QS1_Unanswered_Falls_Through_Same_As_Products()
    {
        var answers = new DiscoveryAnswerSet(new[]
        {
            new DiscoveryAnswer("Q-O1", "200+"),
            new DiscoveryAnswer("Q-O3", "make"),
            new DiscoveryAnswer("Q-O5", "3+"),
        });
        var rec = DiscoveryRecommendationEngine.Recommend(answers);
        // Existing routing — large + multi-site → PRESET-06.
        Assert.Equal("PRESET-06", rec.PresetId);
    }

    // ─── Helper response shapes ────────────────────────────────────────────

    private record QuestionsResponseRow(
        int TotalCount,
        int SelfServeCount,
        int ConsultantDeepdiveCount,
        List<QuestionRow> Questions);

    private record QuestionRow(string Id, string Stage, string Category, string Type, string Text);

    private record RecommendationResponseRow(
        string PresetId,
        string PresetName,
        string PresetDescription,
        double Confidence,
        string ConfidenceLabel,
        string Rationale,
        List<FactorRow> Factors,
        List<AlternativeRow> Alternatives,
        List<DeltaRow> CapabilityDeltas);

    private record FactorRow(string QuestionId, string Description);
    private record AlternativeRow(string PresetId, string PresetName, string DistinguishingRationale);
    private record DeltaRow(string Code, string Name, bool CurrentlyEnabled, bool WillBeEnabled);
}
