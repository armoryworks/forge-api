using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using Forge.Api.Features.SampleShipments;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SampleShipments;

[Collection(CapabilityTestCollection.Name)]
public class CreateSampleShipmentTests(CapabilityTestWebApplicationFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    private static CreateSampleShipmentHandler Handler(AppDbContext db)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);
        return new CreateSampleShipmentHandler(db, clock.Object);
    }

    [Fact]
    public async Task Sends_a_sample_to_a_customer_with_ship_to_tracking_and_follow_up()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Sample Co" };
        var part = new Part { PartNumber = "S-1", Name = "Sample part" };
        db.AddRange(customer, part);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new CreateSampleShipmentCommand(new CreateSampleShipmentRequest(
            null, part.Id, "Sample part", 3, "Rush", customer.Id, "1 Main St, Springfield", "UPS", "1Z999", Now.AddDays(14))),
            CancellationToken.None);

        result.CustomerId.Should().Be(customer.Id);
        result.LeadId.Should().BeNull();
        result.Status.Should().Be("Shipped");
        result.ShippedAt.Should().Be(Now);
        result.ShipToAddress.Should().Be("1 Main St, Springfield");
        result.FollowUpDate.Should().Be(Now.AddDays(14));
        (await db.ActivityLogs.SingleAsync(a => a.Action.StartsWith("sample"))).EntityType.Should().Be("Customer");
    }

    [Fact]
    public async Task A_sample_without_tracking_starts_requested_on_the_lead()
    {
        using var db = TestDbContextFactory.Create();
        var lead = new Lead { CompanyName = "Prospect" };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new CreateSampleShipmentCommand(new CreateSampleShipmentRequest(
            lead.Id, null, "Prototype", 1, null)), CancellationToken.None);

        result.Status.Should().Be("Requested");
        result.ShippedAt.Should().BeNull();
        (await db.ActivityLogs.SingleAsync(a => a.Action.StartsWith("sample"))).EntityType.Should().Be("Lead");
    }

    [Fact]
    public void A_sample_needs_a_lead_or_a_customer_and_a_part_or_description()
    {
        var validator = new CreateSampleShipmentValidator();

        validator.Validate(new CreateSampleShipmentCommand(new CreateSampleShipmentRequest(null, null, "X", 1, null)))
            .IsValid.Should().BeFalse();
        validator.Validate(new CreateSampleShipmentCommand(new CreateSampleShipmentRequest(5, null, null, 1, null)))
            .IsValid.Should().BeFalse();
        validator.Validate(new CreateSampleShipmentCommand(new CreateSampleShipmentRequest(null, null, "X", 1, null, CustomerId: 3)))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_customer_is_not_found()
    {
        using var db = TestDbContextFactory.Create();

        var act = () => Handler(db).Handle(new CreateSampleShipmentCommand(new CreateSampleShipmentRequest(
            null, null, "X", 1, null, CustomerId: 404)), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Theory]
    [InlineData("PM", HttpStatusCode.Created)]
    [InlineData("OfficeManager", HttpStatusCode.Forbidden)]
    public async Task Sample_roles_send_samples_and_others_are_refused(string role, HttpStatusCode expected)
    {
        var admin = Client("Admin");
        bool wasEnabled;
        int customerId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            wasEnabled = (await db.Capabilities.AsNoTracking().SingleAsync(c => c.Code == "CAP-O2C-LEAD")).Enabled;
            var customer = new Customer { Name = $"Samp {Guid.NewGuid():N}"[..16] };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }
        await admin.PutAsync("/api/v1/capabilities/CAP-O2C-LEAD/enabled", JsonContent.Create(new { enabled = true }));
        try
        {
            var response = await Client(role).PostAsJsonAsync("/api/v1/sample-shipments",
                new { customerId, partDescription = "Coupon", quantity = 2 });

            response.StatusCode.Should().Be(expected);
            var list = await admin.GetAsync($"/api/v1/sample-shipments?customerId={customerId}");
            var rows = await list.Content.ReadFromJsonAsync<List<SampleShipmentResponseModel>>();
            rows.Should().HaveCount(expected == HttpStatusCode.Created ? 1 : 0);
        }
        finally
        {
            if (!wasEnabled)
                await admin.PutAsync("/api/v1/capabilities/CAP-O2C-LEAD/enabled", JsonContent.Create(new { enabled = false }));
        }
    }
}
