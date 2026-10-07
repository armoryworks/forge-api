using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Authorization;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.ShopFloor;

/// <summary>
/// Region 3 · Shop Floor RED tests (see ../README.md). Ship-gate authz findings:
/// SF-04 (complete-job) and SF-05 (assign-job) were class-[Authorize] only (any
/// authenticated role) — complete-job jumped to the final irreversible stage and
/// assign-job let anyone steal any job. These assert a ProductionWorker is rejected
/// (403), and that complete-job and the scan advance move one shop-floor status only. CAP-MFG-SHOPFLOOR is on. SF-10: the clock punch needs the signed-in worker's
/// JWT (a device token alone is 401) and only Admin/Manager may punch for someone else.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class ShopFloorRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public ShopFloorRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "5");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    private async Task<string> SeedTerminalAsync()
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var team = new Team { Name = $"Team {Guid.NewGuid():N}" };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        var token = Guid.NewGuid().ToString("N");
        db.KioskTerminals.Add(new KioskTerminal { Name = "Floor", DeviceToken = token, TeamId = team.Id, ConfiguredByUserId = 1 });
        await db.SaveChangesAsync();
        return token;
    }

    private async Task<(int JobId, int InProductionId, int QcId)> SeedProductionJobAsync()
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var code = Guid.NewGuid().ToString("N")[..8];
        var track = new TrackType { Name = $"Production {code}", Code = $"prod_{code}", IsActive = true };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();

        var inProduction = new JobStage { TrackTypeId = track.Id, Name = "In Production", Code = "in_production", SortOrder = 6, IsShopFloor = true };
        var qc = new JobStage { TrackTypeId = track.Id, Name = "QC/Review", Code = "qc_review", SortOrder = 7, IsShopFloor = true };
        var paid = new JobStage { TrackTypeId = track.Id, Name = "Payment Received", Code = "payment_received", SortOrder = 11, IsIrreversible = true };
        db.JobStages.AddRange(inProduction, qc, paid);
        await db.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = $"JOB-{code}",
            Title = "Bracket",
            TrackTypeId = track.Id,
            CurrentStageId = inProduction.Id,
            Priority = JobPriority.Normal,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return (job.Id, inProduction.Id, qc.Id);
    }

    private async Task<(int JobId, int ShippedId)> SeedShippedJobWithLinkedCustomerAsync()
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var code = Guid.NewGuid().ToString("N")[..8];
        var customer = new Customer { Name = $"Customer {code}", ExternalId = $"QB-{code}" };
        var track = new TrackType { Name = $"Production {code}", Code = $"prod_{code}", IsActive = true };
        db.Customers.Add(customer);
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();

        var shipped = new JobStage { TrackTypeId = track.Id, Name = "Shipped", Code = "shipped", SortOrder = 8, IsShopFloor = true, AccountingDocumentType = AccountingDocumentType.Invoice };
        var invoiced = new JobStage { TrackTypeId = track.Id, Name = "Invoiced/Sent", Code = "invoiced_sent", SortOrder = 9, IsShopFloor = false, IsIrreversible = true, AccountingDocumentType = AccountingDocumentType.Invoice };
        var paid = new JobStage { TrackTypeId = track.Id, Name = "Payment Received", Code = "payment_received", SortOrder = 11, IsIrreversible = true, AccountingDocumentType = AccountingDocumentType.Payment };
        db.JobStages.AddRange(shipped, invoiced, paid);
        await db.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = $"JOB-{code}",
            Title = "Bracket",
            TrackTypeId = track.Id,
            CurrentStageId = shipped.Id,
            CustomerId = customer.Id,
            Priority = JobPriority.Normal,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return (job.Id, shipped.Id);
    }

    private async Task<int> CurrentStageAsync(int jobId)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.CurrentStageId).FirstAsync();
    }

    [Fact] // SF-04 GREEN — complete-job now requires Admin/Manager
    public async Task Production_worker_cannot_complete_a_job_from_the_kiosk()
    {
        var response = await AuthClient("ProductionWorker")
            .PostAsync("/api/v1/display/shop-floor/complete-job", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "completing a job from the kiosk must require Admin/Manager / supervisor approval");
    }

    [Fact] // SF-05 GREEN — assign-job now requires Admin/Manager
    public async Task Production_worker_cannot_assign_a_job_from_the_kiosk()
    {
        var response = await AuthClient("ProductionWorker")
            .PostAsync("/api/v1/display/shop-floor/assign-job", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "assigning/stealing a job must require Admin/Manager, not any authenticated user");
    }

    [Fact]
    public async Task Complete_job_moves_one_status_and_never_to_payment_received()
    {
        var (jobId, _, qcId) = await SeedProductionJobAsync();

        var response = await AuthClient("Manager")
            .PostAsJsonAsync("/api/v1/display/shop-floor/complete-job", new { jobId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CurrentStageAsync(jobId)).Should().Be(qcId);
    }

    [Fact]
    public async Task Production_worker_can_advance_a_job_through_scan()
    {
        var (jobId, _, qcId) = await SeedProductionJobAsync();

        var response = await AuthClient("ProductionWorker")
            .PostAsync($"/api/v1/display/shop-floor/jobs/{jobId}/advance", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CurrentStageAsync(jobId)).Should().Be(qcId);

        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.JobActivityLogs.AsNoTracking()
            .Where(a => a.JobId == jobId && a.FieldName == "CurrentStageId")
            .SingleAsync();
        log.UserId.Should().Be(1, "the test auth handler signs every caller in as user 1");
    }

    [Fact]
    public async Task A_device_token_alone_cannot_advance_a_job()
    {
        var token = await SeedTerminalAsync();
        var (jobId, inProductionId, _) = await SeedProductionJobAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(KioskTerminalAuthAttribute.HeaderName, token);

        var status = await client.GetAsync($"/api/v1/display/shop-floor/jobs/{jobId}/status");
        status.StatusCode.Should().Be(HttpStatusCode.OK, "reading the status stays on device-token auth");

        var response = await client.PostAsync($"/api/v1/display/shop-floor/jobs/{jobId}/advance", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CurrentStageAsync(jobId)).Should().Be(inProductionId);
    }

    [Theory]
    [InlineData("ProductionWorker", "jobs/{0}/advance")]
    [InlineData("Manager", "complete-job")]
    public async Task A_shipped_job_cannot_move_into_an_office_status_from_the_kiosk(string role, string path)
    {
        var (jobId, shippedId) = await SeedShippedJobWithLinkedCustomerAsync();
        var client = AuthClient(role);
        var url = $"/api/v1/display/shop-floor/{string.Format(path, jobId)}";

        var response = path == "complete-job"
            ? await client.PostAsJsonAsync(url, new { jobId })
            : await client.PostAsync(url, null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("office status");
        (await CurrentStageAsync(jobId)).Should().Be(shippedId);

        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.SyncQueueEntries.AsNoTracking().AnyAsync(e => e.EntityType == "Job" && e.EntityId == jobId))
            .Should().BeFalse();
    }

    [Fact]
    public async Task A_device_token_alone_cannot_punch_the_clock()
    {
        var token = await SeedTerminalAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(KioskTerminalAuthAttribute.HeaderName, token);

        var status = await client.GetAsync("/api/v1/display/shop-floor/clock-status");
        status.StatusCode.Should().Be(HttpStatusCode.OK, "the read endpoints stay on device-token auth");

        var punch = await client.PostAsJsonAsync("/api/v1/display/shop-floor/clock", new { userId = 1, eventType = "ClockIn" });
        punch.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_worker_cannot_punch_for_someone_else()
    {
        var response = await AuthClient("ProductionWorker")
            .PostAsJsonAsync("/api/v1/display/shop-floor/clock", new { userId = 2, eventType = "ClockIn" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
