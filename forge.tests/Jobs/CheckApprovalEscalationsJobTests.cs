using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Notifications;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

public class CheckApprovalEscalationsJobTests
{
    private const int RequesterId = 1;
    private const int ManagerId = 2;
    private const int ControllerId = 3;
    private const int InactiveControllerId = 4;
    private const int OwnerId = 5;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly List<CreateNotificationRequestModel> _sent = [];

    public CheckApprovalEscalationsJobTests()
    {
        _mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationResponseModel>, CancellationToken>((cmd, _) => _sent.Add(((CreateNotificationCommand)cmd).Data))
            .ReturnsAsync((NotificationResponseModel)null!);
    }

    private CheckApprovalEscalationsJob Job() => new(
        new ApprovalService(_db, NullLogger<ApprovalService>.Instance),
        _mediator.Object,
        NullLogger<CheckApprovalEscalationsJob>.Instance);

    private async Task<ApprovalRequest> SeedAsync(DateTimeOffset requestedAt)
    {
        var controller = new IdentityRole<int> { Name = "Controller", NormalizedName = "CONTROLLER" };
        _db.Roles.Add(controller);
        _db.Users.AddRange(
            new ApplicationUser { Id = RequesterId, UserName = "bea", FirstName = "Bea", LastName = "Buyer" },
            new ApplicationUser { Id = ManagerId, UserName = "lena", FirstName = "Lena", LastName = "Manager" },
            new ApplicationUser { Id = ControllerId, UserName = "cora", FirstName = "Cora", LastName = "Controller" },
            new ApplicationUser { Id = InactiveControllerId, UserName = "old", FirstName = "Old", LastName = "Controller", IsActive = false },
            new ApplicationUser { Id = OwnerId, UserName = "ada", FirstName = "Ada", LastName = "Owner" });
        await _db.SaveChangesAsync();
        _db.UserRoles.AddRange(
            new IdentityUserRole<int> { UserId = ControllerId, RoleId = controller.Id },
            new IdentityUserRole<int> { UserId = InactiveControllerId, RoleId = controller.Id });

        var workflow = new ApprovalWorkflow
        {
            Name = "PO over 5k", EntityType = "PurchaseOrder", IsActive = true,
            Steps =
            {
                new ApprovalStep { StepNumber = 1, Name = "Plant manager", ApproverType = ApproverType.SpecificUser, ApproverUserId = ManagerId, EscalationHours = 2 },
                new ApprovalStep { StepNumber = 2, Name = "Controller", ApproverType = ApproverType.Role, ApproverRole = "Controller", EscalationHours = 24 },
                new ApprovalStep { StepNumber = 3, Name = "Owner", ApproverType = ApproverType.SpecificUser, ApproverUserId = OwnerId },
            },
        };
        _db.ApprovalWorkflows.Add(workflow);
        await _db.SaveChangesAsync();

        var request = new ApprovalRequest
        {
            WorkflowId = workflow.Id, EntityType = "PurchaseOrder", EntityId = 900,
            CurrentStepNumber = 1, Status = ApprovalRequestStatus.Pending,
            RequestedById = RequesterId, RequestedAt = requestedAt, Amount = 7400m,
            EntitySummary = "PO PO-00042 — 2 line(s)",
        };
        _db.ApprovalRequests.Add(request);
        await _db.SaveChangesAsync();
        return request;
    }

    [Fact]
    public async Task An_overdue_step_escalates_and_notifies_the_next_steps_active_approvers_once()
    {
        var request = await SeedAsync(DateTimeOffset.UtcNow.AddHours(-3));

        await Job().ExecuteAsync(CancellationToken.None);

        _db.ChangeTracker.Clear();
        var stored = (await _db.ApprovalRequests.FindAsync(request.Id))!;
        stored.Status.Should().Be(ApprovalRequestStatus.Escalated);
        stored.CurrentStepNumber.Should().Be(2);
        _sent.Should().ContainSingle();
        _sent[0].UserId.Should().Be(ControllerId);
        _sent[0].Type.Should().Be("approval_escalated");
        _sent[0].EntityType.Should().Be("PurchaseOrder");
        _sent[0].EntityId.Should().Be(900);
        _sent[0].Message.Should().Contain("PO-00042");

        await Job().ExecuteAsync(CancellationToken.None);

        _sent.Should().ContainSingle("a second run inside the next step's window escalates nothing");
    }

    [Fact]
    public async Task An_already_escalated_request_escalates_again_when_the_next_step_is_overdue()
    {
        var request = await SeedAsync(DateTimeOffset.UtcNow.AddDays(-3));
        request.Status = ApprovalRequestStatus.Escalated;
        request.CurrentStepNumber = 2;
        request.EscalatedAt = DateTimeOffset.UtcNow.AddHours(-25);
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync(CancellationToken.None);

        _db.ChangeTracker.Clear();
        (await _db.ApprovalRequests.FindAsync(request.Id))!.CurrentStepNumber.Should().Be(3);
        _sent.Should().ContainSingle().Which.UserId.Should().Be(OwnerId);
    }

    [Fact]
    public async Task A_step_reached_by_approval_gets_its_full_window_before_escalating()
    {
        var request = await SeedAsync(DateTimeOffset.UtcNow.AddDays(-3));
        request.CurrentStepNumber = 2;
        _db.ApprovalDecisions.Add(new ApprovalDecision
        {
            RequestId = request.Id, StepNumber = 1, DecidedById = ManagerId,
            Decision = ApprovalDecisionType.Approve, DecidedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync(CancellationToken.None);

        _db.ChangeTracker.Clear();
        var stored = (await _db.ApprovalRequests.FindAsync(request.Id))!;
        stored.Status.Should().Be(ApprovalRequestStatus.Pending);
        stored.CurrentStepNumber.Should().Be(2);
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_request_inside_its_window_is_left_alone()
    {
        var request = await SeedAsync(DateTimeOffset.UtcNow.AddMinutes(-30));

        await Job().ExecuteAsync(CancellationToken.None);

        _db.ChangeTracker.Clear();
        (await _db.ApprovalRequests.FindAsync(request.Id))!.Status.Should().Be(ApprovalRequestStatus.Pending);
        _sent.Should().BeEmpty();
    }
}
