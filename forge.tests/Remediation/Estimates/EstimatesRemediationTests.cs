using System.Net;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Estimates;

/// <summary>
/// Region 2 · Estimates RED tests (see ../README.md).
/// Findings: BE-3 (ConvertEstimateToQuote yields a zero-line quote — EstimatedAmount is
/// not transferred to a quote line), E-1 (EstimateFormDialog's compute path has no
/// backend — POST /estimates/{id}/compute does not exist). CAP-O2C-QUOTE is default-on.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class EstimatesRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public EstimatesRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    [Fact(Skip = "RED: BE-3 — converting an estimate yields a zero-line quote; the EstimatedAmount " +
                 "is not carried onto a quote line. Remove Skip when the converted quote has ≥1 line.")]
    public async Task Converting_an_estimate_produces_a_quote_with_at_least_one_line()
    {
        int estimateId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var estimate = new Quote
            {
                CustomerId = 1,
                Type = QuoteType.Estimate,
                Status = QuoteStatus.Accepted,
                Title = "BE3-Estimate",
                EstimatedAmount = 500m,
            };
            db.Quotes.Add(estimate);
            await db.SaveChangesAsync();
            estimateId = estimate.Id;
        }

        await AuthClient().PostAsync($"/api/v1/estimates/{estimateId}/convert", null);

        using var verify = NewScope();
        var db2 = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var converted = db2.Quotes
            .Include(q => q.Lines)
            .FirstOrDefault(q => q.SourceEstimateId == estimateId && q.Type == QuoteType.Quote);

        converted.Should().NotBeNull("the convert should produce a Quote-type row linked to the estimate");
        converted!.Lines.Should().NotBeEmpty(
            "the estimate's amount must become at least one quote line (today the quote converts empty)");
    }

    [Fact(Skip = "RED: E-1 — the estimate compute path is mocked/absent (POST /estimates/{id}/compute " +
                 "does not exist). Remove Skip when the compute endpoint exists (or delete the dead dialog).")]
    public async Task Estimate_compute_endpoint_exists()
    {
        var response = await AuthClient().PostAsync("/api/v1/estimates/1/compute", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "the estimate dialog's compute step needs a real backend, not a mock");
    }
}
