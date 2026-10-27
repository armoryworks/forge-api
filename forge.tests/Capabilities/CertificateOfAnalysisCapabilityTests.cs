using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

namespace Forge.Tests.Capabilities;

/// <summary>
/// CAP-QC-COA is off by default: every certificate endpoint refuses with capability-disabled until it is turned
/// on, and once on the role sets apply.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class CertificateOfAnalysisCapabilityTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient Client(string role = "Admin")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static async Task SetAsync(HttpClient client, string code, bool enabled)
        => (await client.PutAsJsonAsync($"/api/v1/capabilities/{code}/enabled", new { enabled })).EnsureSuccessStatusCode();

    [Theory]
    [InlineData("GET", "/api/v1/certificates-of-analysis?lotId=1")]
    [InlineData("POST", "/api/v1/certificates-of-analysis")]
    [InlineData("POST", "/api/v1/shipments/1/certificates-of-analysis")]
    public async Task Endpoints_are_refused_while_the_capability_is_off(string verb, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(verb), url);
        if (verb == "POST")
            request.Content = JsonContent.Create(new { lotId = 1 });

        var response = await Client().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("capability-disabled");
    }

    [Fact]
    public async Task Once_on_the_list_answers_and_shipping_cannot_generate()
    {
        var admin = Client();
        await SetAsync(admin, "CAP-INV-LOTS", true);
        await SetAsync(admin, "CAP-QC-COA", true);
        try
        {
            (await admin.GetAsync("/api/v1/certificates-of-analysis?lotId=987654")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await Client("OfficeManager").PostAsJsonAsync("/api/v1/certificates-of-analysis", new { lotId = 987654 }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await Client("PM").GetAsync("/api/v1/certificates-of-analysis?lotId=987654"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await Client("Engineer").PostAsJsonAsync("/api/v1/certificates-of-analysis", new { lotId = 987654 }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await SetAsync(admin, "CAP-QC-COA", false);
            await SetAsync(admin, "CAP-INV-LOTS", false);
        }
    }
}
