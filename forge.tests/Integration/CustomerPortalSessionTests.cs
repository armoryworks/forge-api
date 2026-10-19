using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class CustomerPortalSessionTests
{
    private const string FrontendBase = "https://erp.example.test";

    private readonly WebApplicationFactory<Program> _factory;

    public CustomerPortalSessionTests(TestWebApplicationFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Smtp:Host", "");
            b.UseSetting("FrontendBaseUrl", FrontendBase);
        });
    }

    [Fact]
    public async Task MagicLink_PointsAtTheConfiguredFrontend_NotTheCallersOrigin()
    {
        var seeded = await SeedPortalContactAsync("origin-probe@customer.test");
        var anon = _factory.CreateClient();

        var link = await RequestLinkAsync(anon, seeded.Email, "https://attacker.example");

        link.Should().StartWith($"{FrontendBase}/portal/auth/callback?token=");
    }

    [Fact]
    public async Task ExchangedPortalSession_ReadsAndRespondsToQuotes()
    {
        var seeded = await SeedPortalContactAsync("session-probe@customer.test");
        var portal = await PortalClientAsync(seeded.Email);

        (await portal.GetAsync("/api/v1/portal/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await portal.GetAsync("/api/v1/portal/me/dashboard")).StatusCode.Should().Be(HttpStatusCode.OK);
        var quotes = await portal.GetAsync("/api/v1/portal/me/quotes");
        quotes.StatusCode.Should().Be(HttpStatusCode.OK);
        quotes.Headers.GetValues("X-Total-Count").Should().Equal("1");
        (await quotes.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength().Should().Be(1);

        var respond = await portal.PostAsJsonAsync(
            $"/api/v1/portal/me/quotes/{seeded.QuoteId}/respond", new { accepted = true });
        respond.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quote = await db.Quotes.AsNoTracking().SingleAsync(q => q.Id == seeded.QuoteId);
        quote.Status.Should().Be(QuoteStatus.Accepted);
        quote.AcceptedByContactId.Should().Be(seeded.ContactId);
    }

    [Fact]
    public async Task PortalSession_IsNotAnEmployeeSession()
    {
        var seeded = await SeedPortalContactAsync("scope-probe@customer.test");
        var portal = await PortalClientAsync(seeded.Email);

        (await portal.GetAsync("/api/v1/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await portal.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EmployeeToken_IsRefusedOnPortalEndpoints()
    {
        await EnsurePortalCapabilityAsync();
        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            token = scope.ServiceProvider.GetRequiredService<ITokenService>()
                .GenerateToken(1, "staff@forge.test", "Staff", "Member", null, null, ["Admin"]).Token;
        }
        var staff = _factory.CreateClient();
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        (await staff.GetAsync("/api/v1/portal/me/dashboard")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DisablingPortalAccess_EndsTheSession()
    {
        var seeded = await SeedPortalContactAsync("revoke-probe@customer.test");
        var portal = await PortalClientAsync(seeded.Email);
        (await portal.GetAsync("/api/v1/portal/me/dashboard")).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var access = await db.CustomerPortalAccesses.SingleAsync(a => a.ContactId == seeded.ContactId);
            access.IsEnabled = false;
            await db.SaveChangesAsync();
        }

        (await portal.GetAsync("/api/v1/portal/me/dashboard")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> PortalClientAsync(string email)
    {
        var anon = _factory.CreateClient();
        var link = await RequestLinkAsync(anon, email, null);
        var token = Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);

        var exchange = await anon.PostAsJsonAsync("/api/v1/portal/auth/exchange", new { token });
        exchange.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await exchange.Content.ReadFromJsonAsync<JsonElement>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        return client;
    }

    private static async Task<string> RequestLinkAsync(HttpClient client, string email, string? origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/portal/auth/request-link")
        {
            Content = JsonContent.Create(new { email }),
        };
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
            request.Headers.Add("X-Forwarded-Host", new Uri(origin).Host);
        }

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("devLink").GetString()!;
    }

    private async Task EnsurePortalCapabilityAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var capability = await db.Capabilities.SingleAsync(c => c.Code == "CAP-EXT-CUSTOMER-PORTAL");
        if (!capability.Enabled)
        {
            capability.Enabled = true;
            await db.SaveChangesAsync();
        }
        await _factory.Services.GetRequiredService<ICapabilitySnapshotProvider>().RefreshAsync();
    }

    private async Task<(string Email, int ContactId, int QuoteId)> SeedPortalContactAsync(string email)
    {
        await EnsurePortalCapabilityAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var customer = new Customer { Name = $"Portal Probe {Guid.NewGuid():N}" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var contact = new Contact { CustomerId = customer.Id, FirstName = "Pat", LastName = "Buyer", Email = email };
        db.Contacts.Add(contact);
        var quote = new Quote
        {
            CustomerId = customer.Id,
            QuoteNumber = $"Q-{Guid.NewGuid():N}"[..12],
            Status = QuoteStatus.Sent,
        };
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();

        db.CustomerPortalAccesses.Add(new CustomerPortalAccess
        {
            ContactId = contact.Id,
            CustomerId = customer.Id,
            IsEnabled = true,
        });
        await db.SaveChangesAsync();

        return (email, contact.Id, quote.Id);
    }
}
