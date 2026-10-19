using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Approvals;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Approvals;

public class ApprovalAuthorizationTests
{
    private const int RequesterId = 10;
    private const int ManagerId = 20;
    private const int WorkerId = 30;
    private const int ControllerId = 40;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly ApprovalService _service;

    public ApprovalAuthorizationTests()
    {
        _service = new ApprovalService(_db, NullLogger<ApprovalService>.Instance);
    }

    private async Task SeedUsersAsync()
    {
        var manager = new IdentityRole<int> { Name = "Manager", NormalizedName = "MANAGER" };
        var worker = new IdentityRole<int> { Name = "ProductionWorker", NormalizedName = "PRODUCTIONWORKER" };
        var controller = new IdentityRole<int> { Name = "Controller", NormalizedName = "CONTROLLER" };
        _db.Roles.AddRange(manager, worker, controller);
        _db.Users.AddRange(
            new ApplicationUser { Id = RequesterId, UserName = "lena", FirstName = "Lena", LastName = "Requester" },
            new ApplicationUser { Id = ManagerId, UserName = "marco", FirstName = "Marco", LastName = "Manager" },
            new ApplicationUser { Id = WorkerId, UserName = "ben", FirstName = "Ben", LastName = "Worker" },
            new ApplicationUser { Id = ControllerId, UserName = "cora", FirstName = "Cora", LastName = "Controller" });
        await _db.SaveChangesAsync();
        _db.UserRoles.AddRange(
            new IdentityUserRole<int> { UserId = RequesterId, RoleId = manager.Id },
            new IdentityUserRole<int> { UserId = ManagerId, RoleId = manager.Id },
            new IdentityUserRole<int> { UserId = WorkerId, RoleId = worker.Id },
            new IdentityUserRole<int> { UserId = ControllerId, RoleId = controller.Id });
        await _db.SaveChangesAsync();
    }

    private async Task<ApprovalRequest> SeedRequestAsync(string entityType = "PurchaseOrder", ApprovalStep? firstStep = null)
    {
        await SeedUsersAsync();
        var workflow = new ApprovalWorkflow
        {
            Name = "Two-step", EntityType = entityType, IsActive = true,
            Steps =
            {
                firstStep ?? new ApprovalStep { StepNumber = 1, Name = "Plant manager", ApproverType = ApproverType.Role, ApproverRole = "Manager" },
                new ApprovalStep { StepNumber = 2, Name = "Controller", ApproverType = ApproverType.Role, ApproverRole = "Controller" },
            },
        };
        _db.ApprovalWorkflows.Add(workflow);
        await _db.SaveChangesAsync();

        var request = new ApprovalRequest
        {
            WorkflowId = workflow.Id, EntityType = entityType, EntityId = 77,
            CurrentStepNumber = 1, Status = ApprovalRequestStatus.Pending,
            RequestedById = RequesterId, RequestedAt = DateTimeOffset.UtcNow, Amount = 6200m,
        };
        _db.ApprovalRequests.Add(request);
        await _db.SaveChangesAsync();
        return request;
    }

    private ApproveRequestHandler Approve() => new(_service, _db, Mock.Of<IMediator>());
    private RejectRequestHandler Reject() => new(_service, _db, Mock.Of<IMediator>());
    private DelegateRequestHandler Delegate() => new(_service);

    [Fact]
    public async Task A_user_who_is_not_the_step_approver_cannot_approve()
    {
        var request = await SeedRequestAsync();

        var act = () => Approve().Handle(new ApproveRequestCommand(request.Id, WorkerId, "ok"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*not an approver*");
        _db.ChangeTracker.Clear();
        (await _db.ApprovalRequests.FindAsync(request.Id))!.CurrentStepNumber.Should().Be(1);
        _db.ApprovalDecisions.Should().BeEmpty();
    }

    [Fact]
    public async Task The_step_approver_can_approve_and_the_request_moves_on()
    {
        var request = await SeedRequestAsync();

        var result = await Approve().Handle(new ApproveRequestCommand(request.Id, ManagerId, "ok"), CancellationToken.None);

        result.CurrentStepNumber.Should().Be(2);
        var next = () => Approve().Handle(new ApproveRequestCommand(request.Id, ManagerId, "again"), CancellationToken.None);
        await next.Should().ThrowAsync<ForbiddenException>("step 2 belongs to the Controller");
    }

    [Fact]
    public async Task The_requester_cannot_approve_her_own_request_even_with_the_step_role()
    {
        var request = await SeedRequestAsync();

        var act = () => Approve().Handle(new ApproveRequestCommand(request.Id, RequesterId, "self"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*you submitted*");
        (await _service.GetPendingApprovalsAsync(RequesterId, CancellationToken.None)).Should().BeEmpty();
        (await _service.GetPendingApprovalsAsync(ManagerId, CancellationToken.None))
            .Should().ContainSingle(r => r.Id == request.Id);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task An_expense_requester_may_self_approve_only_when_the_expense_setting_allows_it(string setting, bool allowed)
    {
        _db.SystemSettings.Add(new SystemSetting { Key = "expense_self_approval", Value = setting });
        var request = await SeedRequestAsync("Expense");

        var status = await _service.GetActorStatusAsync(request.Id, RequesterId, CancellationToken.None);
        var inbox = await _service.GetPendingApprovalsAsync(RequesterId, CancellationToken.None);

        status.Should().Be(allowed ? ApprovalActorStatus.Allowed : ApprovalActorStatus.OwnRequest);
        inbox.Any(r => r.Id == request.Id).Should().Be(allowed);
    }

    [Fact]
    public async Task The_expense_setting_does_not_open_self_approval_for_other_entity_types()
    {
        _db.SystemSettings.Add(new SystemSetting { Key = "expense_self_approval", Value = "true" });
        var request = await SeedRequestAsync("PurchaseOrder");

        var status = await _service.GetActorStatusAsync(request.Id, RequesterId, CancellationToken.None);

        status.Should().Be(ApprovalActorStatus.OwnRequest);
    }

    [Fact]
    public async Task A_non_approver_cannot_reject_or_delegate()
    {
        var request = await SeedRequestAsync();

        var reject = () => Reject().Handle(new RejectRequestCommand(request.Id, WorkerId, "no"), CancellationToken.None);
        var delegateIt = () => Delegate().Handle(
            new DelegateRequestCommand(request.Id, WorkerId, new DelegateApprovalRequestModel(WorkerId, null)), CancellationToken.None);

        await reject.Should().ThrowAsync<ForbiddenException>();
        await delegateIt.Should().ThrowAsync<ForbiddenException>();
        _db.ApprovalDecisions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_user_the_step_was_delegated_to_can_approve_it()
    {
        var request = await SeedRequestAsync();

        await Delegate().Handle(
            new DelegateRequestCommand(request.Id, ManagerId, new DelegateApprovalRequestModel(WorkerId, "covering")), CancellationToken.None);
        var result = await Approve().Handle(new ApproveRequestCommand(request.Id, WorkerId, "ok"), CancellationToken.None);

        result.CurrentStepNumber.Should().Be(2);
    }

    [Fact]
    public async Task A_specific_user_step_accepts_only_that_user()
    {
        var request = await SeedRequestAsync(firstStep: new ApprovalStep
        {
            StepNumber = 1, Name = "Named approver", ApproverType = ApproverType.SpecificUser, ApproverUserId = ControllerId,
        });

        var byManager = () => Approve().Handle(new ApproveRequestCommand(request.Id, ManagerId, "ok"), CancellationToken.None);
        await byManager.Should().ThrowAsync<ForbiddenException>();

        var result = await Approve().Handle(new ApproveRequestCommand(request.Id, ControllerId, "ok"), CancellationToken.None);
        result.CurrentStepNumber.Should().Be(2);
    }

    [Fact]
    public async Task Current_approver_ids_list_active_role_members_but_not_the_requester()
    {
        var request = await SeedRequestAsync();

        var ids = await _service.GetCurrentApproverIdsAsync(request.Id, CancellationToken.None);

        ids.Should().Equal(ManagerId);
    }
}
