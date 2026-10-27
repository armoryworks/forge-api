using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Approvals;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Approvals;

public class SubmitForApprovalDuplicateTests
{
    private const int RequesterId = 10;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly SubmitForApprovalHandler _handler;

    public SubmitForApprovalDuplicateTests()
    {
        _handler = new SubmitForApprovalHandler(
            new ApprovalService(_db, NullLogger<ApprovalService>.Instance), Mock.Of<IPublisher>(), _db);
        _db.ApprovalWorkflows.Add(new ApprovalWorkflow
        {
            Name = "Manager Review", EntityType = "PurchaseOrder", IsActive = true,
            Steps = { new ApprovalStep { StepNumber = 1, Name = "Manager", ApproverType = ApproverType.Role, ApproverRole = "Manager" } },
        });
        _db.SaveChanges();
    }

    private Task<ApprovalRequestResponseModel?> SubmitAsync(int entityId = 1) =>
        _handler.Handle(
            new SubmitForApprovalCommand(new SubmitApprovalRequestModel("PurchaseOrder", entityId, 900m, "PO"), RequesterId),
            CancellationToken.None);

    [Fact]
    public async Task A_second_submit_while_the_first_is_pending_is_refused()
    {
        var first = await SubmitAsync();

        var act = () => SubmitAsync();

        var thrown = await act.Should().ThrowAsync<ApprovalAlreadyPendingException>();
        thrown.Which.RequestId.Should().Be(first!.Id);
        _db.ApprovalRequests.Count(r => r.EntityType == "PurchaseOrder" && r.EntityId == 1).Should().Be(1);
    }

    [Fact]
    public async Task An_escalated_request_also_blocks_a_resubmit()
    {
        var first = await SubmitAsync();
        var stored = _db.ApprovalRequests.Single(r => r.Id == first!.Id);
        stored.Status = ApprovalRequestStatus.Escalated;
        await _db.SaveChangesAsync();

        var act = () => SubmitAsync();

        await act.Should().ThrowAsync<ApprovalAlreadyPendingException>();
    }

    [Theory]
    [InlineData(ApprovalRequestStatus.Rejected)]
    [InlineData(ApprovalRequestStatus.Approved)]
    [InlineData(ApprovalRequestStatus.Cancelled)]
    public async Task A_finished_request_does_not_block_a_new_one(ApprovalRequestStatus finished)
    {
        var first = await SubmitAsync();
        var stored = _db.ApprovalRequests.Single(r => r.Id == first!.Id);
        stored.Status = finished;
        await _db.SaveChangesAsync();

        var second = await SubmitAsync();

        second.Should().NotBeNull();
        second!.Id.Should().NotBe(first!.Id);
    }

    [Fact]
    public async Task A_pending_request_on_another_record_does_not_block()
    {
        await SubmitAsync(entityId: 1);

        var other = await SubmitAsync(entityId: 2);

        other.Should().NotBeNull();
    }
}
