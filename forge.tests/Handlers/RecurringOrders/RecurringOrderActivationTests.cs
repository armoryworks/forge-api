using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.RecurringOrders;

[Collection(CapabilityTestCollection.Name)]
public class RecurringOrderActivationTests(CapabilityTestWebApplicationFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 6, 0, 0, TimeSpan.Zero);

    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static IClock ClockAt(DateTimeOffset now)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(now);
        return clock.Object;
    }

    private static async Task<int> SeedAsync(AppDbContext db, bool isActive, DateTimeOffset next, int intervalDays = 7)
    {
        var customer = new Customer { Name = $"RO {Guid.NewGuid():N}"[..20] };
        var part = new Part { PartNumber = $"RO-{Guid.NewGuid():N}"[..12], Name = "RO part" };
        db.Customers.Add(customer);
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        var ro = new RecurringOrder
        {
            Name = "Weekly",
            CustomerId = customer.Id,
            IntervalDays = intervalDays,
            NextGenerationDate = next,
            IsActive = isActive,
            Lines = { new RecurringOrderLine { PartId = part.Id, Description = "Widget", Quantity = 1m, UnitPrice = 5m, LineNumber = 1 } },
        };
        db.RecurringOrders.Add(ro);
        await db.SaveChangesAsync();
        return ro.Id;
    }

    [Fact]
    public async Task Generation_job_skips_inactive_templates()
    {
        using var db = TestDbContextFactory.Create();
        var active = await SeedAsync(db, true, Now.AddDays(-1));
        var paused = await SeedAsync(db, false, Now.AddDays(-1));
        var identifiers = new Mock<IBusinessIdentifierService>();
        identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());

        await new RecurringOrderJob(db, identifiers.Object, ClockAt(Now), NullLogger<RecurringOrderJob>.Instance)
            .GenerateDueOrdersAsync();

        db.SalesOrders.Should().ContainSingle().Which.Notes.Should().Contain("Weekly");
        db.RecurringOrders.Single(r => r.Id == active).LastGeneratedDate.Should().Be(Now);
        db.RecurringOrders.Single(r => r.Id == paused).LastGeneratedDate.Should().BeNull();
    }

    [Fact]
    public async Task Resuming_moves_a_past_next_run_forward_by_whole_intervals()
    {
        using var db = TestDbContextFactory.Create();
        var id = await SeedAsync(db, false, Now.AddDays(-10), intervalDays: 7);
        var mediator = new Mock<MediatR.IMediator>();

        await new Forge.Api.Features.RecurringOrders.SetRecurringOrderActiveHandler(
                new Forge.Data.Repositories.RecurringOrderRepository(db), db, ClockAt(Now), mediator.Object)
            .Handle(new Forge.Api.Features.RecurringOrders.SetRecurringOrderActiveCommand(id, true), CancellationToken.None);

        var ro = db.RecurringOrders.Single(r => r.Id == id);
        ro.IsActive.Should().BeTrue();
        ro.NextGenerationDate.Should().Be(Now.AddDays(4));
        db.ActivityLogs.Should().Contain(a => a.EntityType == "RecurringOrder" && a.Action == "activated");
    }

    [Fact]
    public async Task Admin_pauses_and_resumes_a_template()
    {
        var admin = Client("Admin");
        await admin.PutAsync("/api/v1/capabilities/CAP-O2C-RECURRING/enabled", JsonContent.Create(new { enabled = true }));
        int id;
        using (var scope = factory.Services.CreateScope())
            id = await SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), true, DateTimeOffset.UtcNow.AddDays(3));

        var paused = await admin.PostAsync($"/api/v1/recurring-orders/{id}/deactivate", null);
        paused.StatusCode.Should().Be(HttpStatusCode.OK);
        (await paused.Content.ReadFromJsonAsync<RecurringOrderDetailResponseModel>())!.IsActive.Should().BeFalse();

        var resumed = await admin.PostAsync($"/api/v1/recurring-orders/{id}/activate", null);
        resumed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resumed.Content.ReadFromJsonAsync<RecurringOrderDetailResponseModel>())!.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("PM")]
    [InlineData("ProductionWorker")]
    public async Task Roles_outside_the_recurring_set_cannot_pause_templates(string role)
    {
        await Client("Admin").PutAsync("/api/v1/capabilities/CAP-O2C-RECURRING/enabled", JsonContent.Create(new { enabled = true }));

        var response = await Client(role).PostAsync("/api/v1/recurring-orders/1/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
