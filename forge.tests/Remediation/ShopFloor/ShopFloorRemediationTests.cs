using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.ShopFloor;

/// <summary>
/// Region 3 · Shop Floor RED tests (see ../README.md). Ship-gate authz findings:
/// SF-04 (complete-job) and SF-05 (assign-job) are class-[Authorize] only (any
/// authenticated role) — complete-job jumps to the final irreversible stage and
/// assign-job lets anyone steal any job. These assert a ProductionWorker is rejected
/// (403). CAP-MFG-SHOPFLOOR is on. SF-10 (clock — AllowAnonymous+KioskTerminalAuth, no
/// role evaluated) needs a PIN/JWT-tether fix, not a role-403; tracked in the catalog.
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
            "completing a job (irreversible) must require Admin/Manager / supervisor approval");
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
    }
}
