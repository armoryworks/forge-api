using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Features.Approvals;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Approvals;

public class ApprovalWorkflowMatchingTests
{
    private const int RequesterId = 10;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly ApprovalService _service;

    public ApprovalWorkflowMatchingTests()
    {
        _service = new ApprovalService(_db, NullLogger<ApprovalService>.Instance);
    }

    private async Task<ApprovalWorkflow> SeedWorkflowAsync(string name, string? conditions, decimal? autoApproveBelow, DateTimeOffset createdAt)
    {
        var workflow = new ApprovalWorkflow
        {
            Name = name, EntityType = "PurchaseOrder", IsActive = true,
            ActivationConditionsJson = conditions, CreatedAt = createdAt,
            Steps =
            {
                new ApprovalStep { StepNumber = 1, Name = "Manager", ApproverType = ApproverType.Role, ApproverRole = "Manager", AutoApproveBelow = autoApproveBelow },
            },
        };
        _db.ApprovalWorkflows.Add(workflow);
        await _db.SaveChangesAsync();
        return workflow;
    }

    private Task<ApprovalRequest?> SubmitAsync(decimal amount) =>
        _service.SubmitForApprovalAsync("PurchaseOrder", 1, RequesterId, amount, "PO", CancellationToken.None);

    [Fact]
    public async Task A_po_below_the_tier_threshold_goes_to_the_catch_all_workflow_even_when_the_tier_is_newer()
    {
        var managerReview = await SeedWorkflowAsync("Manager Review", null, 500m, DateTimeOffset.UtcNow.AddDays(-2));
        await SeedWorkflowAsync("PO over $5,000", "{\"amountGreaterThan\":5000}", 5000m, DateTimeOffset.UtcNow.AddDays(-1));

        var request = await SubmitAsync(1.05m);

        request!.WorkflowId.Should().Be(managerReview.Id);
        request.Status.Should().Be(ApprovalRequestStatus.AutoApproved);
    }

    [Fact]
    public async Task A_po_above_the_tier_threshold_goes_to_the_tier_even_when_the_catch_all_is_newer()
    {
        await SeedWorkflowAsync("Manager Review", null, 500m, DateTimeOffset.UtcNow.AddDays(-1));
        var tier = await SeedWorkflowAsync("PO over $5,000", "{\"amountGreaterThan\":5000}", null, DateTimeOffset.UtcNow.AddDays(-2));

        var request = await SubmitAsync(6000m);

        request!.WorkflowId.Should().Be(tier.Id);
        request.Status.Should().Be(ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task The_highest_matching_threshold_wins()
    {
        await SeedWorkflowAsync("Over 1,000", "{\"amountGreaterThan\":1000}", null, DateTimeOffset.UtcNow.AddDays(-1));
        var high = await SeedWorkflowAsync("Over 10,000", "{\"amountGreaterThan\":10000}", null, DateTimeOffset.UtcNow.AddDays(-2));

        var request = await SubmitAsync(20000m);

        request!.WorkflowId.Should().Be(high.Id);
    }

    [Fact]
    public async Task A_legacy_unknown_key_workflow_does_not_catch_a_po_another_workflow_matches()
    {
        var managerReview = await SeedWorkflowAsync("Manager Review", null, 500m, DateTimeOffset.UtcNow.AddDays(-2));
        await SeedWorkflowAsync("PO over $5,000", "{\"minAmount\":5000}", 5000m, DateTimeOffset.UtcNow.AddDays(-1));

        var request = await SubmitAsync(1.05m);

        request!.WorkflowId.Should().Be(managerReview.Id);
    }

    [Fact]
    public async Task A_legacy_malformed_workflow_does_not_catch_a_po_another_workflow_matches()
    {
        var managerReview = await SeedWorkflowAsync("Manager Review", null, null, DateTimeOffset.UtcNow.AddDays(-2));
        await SeedWorkflowAsync("Broken", "{not json", 5000m, DateTimeOffset.UtcNow.AddDays(-1));

        var request = await SubmitAsync(800m);

        request!.WorkflowId.Should().Be(managerReview.Id);
        request.Status.Should().Be(ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task A_lone_malformed_workflow_still_governs_so_the_po_is_not_left_without_approval()
    {
        var broken = await SeedWorkflowAsync("PO over $5,000", "{\"minAmount\":5000}", null, DateTimeOffset.UtcNow);

        var request = await SubmitAsync(1.05m);

        request!.WorkflowId.Should().Be(broken.Id);
        request.Status.Should().Be(ApprovalRequestStatus.Pending);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("{}", true)]
    [InlineData("{\"amountGreaterThan\":5000}", true)]
    [InlineData("{\"AmountGreaterThan\":0}", true)]
    [InlineData("{\"amountGreaterThan\":null}", true)]
    [InlineData("{\"minAmount\":5000}", false)]
    [InlineData("{\"amountGreaterThan\":5000,\"currency\":\"USD\"}", false)]
    [InlineData("{\"amountGreaterThan\":-1}", false)]
    [InlineData("{\"amountGreaterThan\":\"5000\"}", false)]
    [InlineData("[5000]", false)]
    [InlineData("{not json", false)]
    public void The_create_and_update_validators_accept_only_supported_conditions(string? conditions, bool valid)
    {
        var data = new CreateApprovalWorkflowRequestModel("PO tier", "PurchaseOrder", null, conditions,
            [new CreateApprovalStepRequestModel(1, "Manager", "Role", null, "Manager", false, null, null, false, false)]);

        new CreateApprovalWorkflowValidator().Validate(new CreateApprovalWorkflowCommand(data)).IsValid.Should().Be(valid);
        new UpdateApprovalWorkflowValidator().Validate(new UpdateApprovalWorkflowCommand(1, data)).IsValid.Should().Be(valid);
    }
}
