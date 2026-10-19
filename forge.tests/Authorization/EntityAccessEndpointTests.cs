using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Authorization;

/// <summary>
/// The cross-cutting files and activity routes through the real pipeline: a role outside
/// the record's owning controller gets 403, and a role inside it still gets through.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class EntityAccessEndpointTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient As(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "5");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Theory]
    [InlineData("/api/v1/Invoice/1/notes")]
    [InlineData("/api/v1/Invoice/1/activity")]
    [InlineData("/api/v1/Invoice/1/history")]
    [InlineData("/api/v1/invoices/1/files")]
    public async Task A_production_worker_cannot_read_an_invoices_notes_activity_or_files(string url)
    {
        (await As("ProductionWorker").GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await As("OfficeManager").GetAsync(url)).StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_production_worker_cannot_comment_on_an_invoice_but_can_on_a_job()
    {
        var body = new { comment = "hello", mentionedUserIds = Array.Empty<int>() };

        (await As("ProductionWorker").PostAsync("/api/v1/Invoice/1/comments", JsonContent.Create(body)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await As("ProductionWorker").PostAsync("/api/v1/Job/1/comments", JsonContent.Create(body)))
            .StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_production_worker_cannot_delete_an_invoice_note()
    {
        (await As("ProductionWorker").DeleteAsync("/api/v1/Invoice/1/notes/1"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
