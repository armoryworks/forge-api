using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MediatR;
using Moq;

using Forge.Api.Features.Approvals;
using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Features.Expenses;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Expenses;

/// <summary>
/// F-26B-05 (issue #13) — the expense decision-PATCH must NOT bypass the configured
/// multi-step approval workflow. Proves:
/// (a) a direct PATCH→Approved is rejected (409 / InvalidOperationException) while a Pending
///     ApprovalRequest governs the expense;
/// (b) driving the workflow to its terminal approval (ApproveAsync via the approve handler →
///     ApprovalCompletedEvent → reaction handler → UpdateExpenseStatusCommand) sets the expense
///     Status == Approved and runs the QBO sync enqueue (the existing side-effect block);
/// (c) PATCH→Approved still succeeds when NO ApprovalRequest governs the expense (small-shop case);
/// (d) a reject through the workflow sets the expense Rejected.
/// The end-to-end MediatR pipeline is impractical in this unit harness, so (b)/(d) wire the
/// reaction handler to a real <see cref="UpdateExpenseStatusHandler"/> through a forwarding
/// <see cref="IMediator"/> double, and the approve/reject handlers are exercised directly.
/// </summary>
public class ExpenseApprovalWorkflowBypassTests
{
    private const int ApproverUserId = 7;
    private const int OwnerUserId = 42;

    private static IHttpContextAccessor HttpContextFor(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        return new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
    }

    private static async Task<Expense> SeedExpenseAsync(AppDbContext db, ExpenseStatus status = ExpenseStatus.Pending)
    {
        var expense = new Expense
        {
            UserId = OwnerUserId, Amount = 250m, Category = "Travel",
            Description = "Conference airfare", Status = status,
            ExpenseDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        return expense;
    }

    /// <summary>Seeds a single-step (manager) workflow + a Pending request governing the expense.</summary>
    private static async Task<ApprovalRequest> SeedPendingRequestAsync(AppDbContext db, int expenseId, decimal amount)
    {
        var workflow = new ApprovalWorkflow
        {
            Name = "Expense approval", EntityType = "Expense", IsActive = true,
            Steps =
            {
                new ApprovalStep { StepNumber = 1, Name = "Manager", ApproverType = ApproverType.Role, ApproverRole = "Manager" },
            },
        };
        db.ApprovalWorkflows.Add(workflow);
        await db.SaveChangesAsync();

        var request = new ApprovalRequest
        {
            WorkflowId = workflow.Id, EntityType = "Expense", EntityId = expenseId,
            CurrentStepNumber = 1, Status = ApprovalRequestStatus.Pending,
            RequestedById = OwnerUserId, RequestedAt = DateTimeOffset.UtcNow, Amount = amount,
        };
        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    /// <summary>
    /// A real UpdateExpenseStatusHandler over the given db, with a connected accounting provider
    /// so the approved transition reaches the QBO sync enqueue. <paramref name="enqueued"/> captures
    /// whether the CreateExpense sync was enqueued. <paramref name="actingUserId"/> is the HTTP
    /// principal (the controller-path fallback actor).
    /// </summary>
    private static UpdateExpenseStatusHandler RealStatusHandler(
        AppDbContext db, List<int> enqueued, int actingUserId = ApproverUserId)
    {
        var provider = new Mock<IAccountingService>();
        provider.Setup(p => p.GetSyncStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountingSyncStatus(Connected: true, LastSyncAt: null, QueueDepth: 0, FailedCount: 0));
        var providerFactory = new Mock<IAccountingProviderFactory>();
        providerFactory.Setup(f => f.GetActiveProviderAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider.Object);

        var syncQueue = new Mock<ISyncQueueRepository>();
        syncQueue.Setup(q => q.EnqueueAsync("Expense", It.IsAny<int>(), "CreateExpense", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, string, string?, CancellationToken>((_, id, _, _, _) => enqueued.Add(id))
            .ReturnsAsync(new SyncQueueEntry());

        return new UpdateExpenseStatusHandler(
            new ExpenseRepository(db),
            HttpContextFor(actingUserId),
            syncQueue.Object,
            providerFactory.Object,
            NullLogger<UpdateExpenseStatusHandler>.Instance,
            apPosting: null,
            billPromotion: null,
            db: db);
    }

    /// <summary>An IMediator double that forwards UpdateExpenseStatusCommand to a real handler.</summary>
    private static IMediator ForwardingMediator(UpdateExpenseStatusHandler statusHandler)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<UpdateExpenseStatusCommand>(), It.IsAny<CancellationToken>()))
            .Returns<UpdateExpenseStatusCommand, CancellationToken>((cmd, ct) => statusHandler.Handle(cmd, ct));
        return mediator.Object;
    }

    // ───────────────────────────── (a) guard blocks the bypass ─────────────────────────────

    [Fact] // F-26B-05 — direct PATCH→Approved is rejected while a Pending workflow request exists
    public async Task PatchToApproved_isRejected_whenAPendingApprovalRequestGovernsTheExpense()
    {
        using var db = TestDbContextFactory.Create();
        var expense = await SeedExpenseAsync(db);
        await SeedPendingRequestAsync(db, expense.Id, expense.Amount);

        var handler = RealStatusHandler(db, []);
        var command = new UpdateExpenseStatusCommand(
            expense.Id, new UpdateExpenseStatusRequestModel(ExpenseStatus.Approved, null));

        var act = () => handler.Handle(command, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*approval workflow*",
                "a premature decision-PATCH must be blocked while the workflow is still pending (maps to 409)");

        // status unchanged
        (await db.Expenses.FindAsync(expense.Id))!.Status.Should().Be(ExpenseStatus.Pending);
    }

    [Fact] // an Escalated request is also non-terminal → still blocks the bypass
    public async Task PatchToRejected_isRejected_whenAnEscalatedApprovalRequestGovernsTheExpense()
    {
        using var db = TestDbContextFactory.Create();
        var expense = await SeedExpenseAsync(db);
        var request = await SeedPendingRequestAsync(db, expense.Id, expense.Amount);
        request.Status = ApprovalRequestStatus.Escalated;
        await db.SaveChangesAsync();

        var handler = RealStatusHandler(db, []);
        var command = new UpdateExpenseStatusCommand(
            expense.Id, new UpdateExpenseStatusRequestModel(ExpenseStatus.Rejected, "Not this quarter, please"));

        var act = () => handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ───────────────────────── (c) no-workflow small-shop case proceeds ─────────────────────────

    [Fact] // F-26B-05 — PATCH→Approved still succeeds when NO ApprovalRequest governs the expense
    public async Task PatchToApproved_succeeds_whenNoApprovalRequestGovernsTheExpense()
    {
        using var db = TestDbContextFactory.Create();
        var expense = await SeedExpenseAsync(db);

        var enqueued = new List<int>();
        var handler = RealStatusHandler(db, enqueued);
        var command = new UpdateExpenseStatusCommand(
            expense.Id, new UpdateExpenseStatusRequestModel(ExpenseStatus.Approved, null));

        await handler.Handle(command, CancellationToken.None);

        (await db.Expenses.FindAsync(expense.Id))!.Status.Should().Be(ExpenseStatus.Approved,
            "with no governing workflow, the direct decision-PATCH is allowed (unchanged behavior)");
    }

    [Fact] // a terminal (already-Approved) governing request does NOT block the event-driven sync
    public async Task PatchToApproved_succeeds_whenTheGoverningRequestIsAlreadyTerminal()
    {
        using var db = TestDbContextFactory.Create();
        var expense = await SeedExpenseAsync(db);
        var request = await SeedPendingRequestAsync(db, expense.Id, expense.Amount);
        request.Status = ApprovalRequestStatus.Approved;
        request.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var handler = RealStatusHandler(db, []);
        var command = new UpdateExpenseStatusCommand(
            expense.Id, new UpdateExpenseStatusRequestModel(ExpenseStatus.Approved, null), ActorUserId: ApproverUserId);

        await handler.Handle(command, CancellationToken.None);

        (await db.Expenses.FindAsync(expense.Id))!.Status.Should().Be(ExpenseStatus.Approved,
            "a terminal request is not in {Pending, Escalated} so the guard allows the sync");
    }

    // ───────── (b) terminal approval → event → reaction handler → command (+ QBO enqueue) ─────────

    [Fact] // F-26B-05 — driving the workflow to terminal approval syncs the expense to Approved + enqueues QBO
    public async Task TerminalApproval_throughTheApproveHandler_setsExpenseApproved_andEnqueuesSync()
    {
        using var db = TestDbContextFactory.Create();
        var expense = await SeedExpenseAsync(db);
        var request = await SeedPendingRequestAsync(db, expense.Id, expense.Amount);

        var enqueued = new List<int>();
        var statusHandler = RealStatusHandler(db, enqueued);
        var reactionHandler = new OnApprovalCompleted_UpdateExpenseStatus(
            ForwardingMediator(statusHandler), NullLogger<OnApprovalCompleted_UpdateExpenseStatus>.Instance);

        // The approve handler publishes ApprovalCompletedEvent (terminal-only) to a publisher that
        // routes to the reaction handler — mirroring the production ResilientNotificationPublisher.
        var publisher = new Mock<IMediator>();
        publisher.Setup(p => p.Publish(It.IsAny<ApprovalCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns<ApprovalCompletedEvent, CancellationToken>((evt, ct) => reactionHandler.Handle(evt, ct));

        var approveHandler = new ApproveRequestHandler(
            new ApprovalService(db, NullLogger<ApprovalService>.Instance), db, publisher.Object);

        await approveHandler.Handle(new ApproveRequestCommand(request.Id, ApproverUserId, "looks good"), CancellationToken.None);

        // The single-step workflow's only step completed → request terminal, expense synced.
        (await db.ApprovalRequests.FindAsync(request.Id))!.Status.Should().Be(ApprovalRequestStatus.Approved);
        var updated = (await db.Expenses.FindAsync(expense.Id))!;
        updated.Status.Should().Be(ExpenseStatus.Approved,
            "the terminal approval must flow through the event chain to the expense status");
        updated.ApprovedBy.Should().Be(ApproverUserId, "the terminal decision's DecidedById is carried as the approver");
        enqueued.Should().Contain(expense.Id, "the approved transition runs the existing QBO sync enqueue");
    }

    [Fact] // an INTERMEDIATE step advance must NOT publish the completion event (no premature sync)
    public async Task IntermediateStepApproval_doesNotPublishCompletion()
    {
        using var db = TestDbContextFactory.Create();
        var expense = await SeedExpenseAsync(db);

        // Two-step workflow: approving step 1 only advances to step 2 — not terminal.
        var workflow = new ApprovalWorkflow
        {
            Name = "Two-step", EntityType = "Expense", IsActive = true,
            Steps =
            {
                new ApprovalStep { StepNumber = 1, Name = "Manager", ApproverType = ApproverType.Role, ApproverRole = "Manager" },
                new ApprovalStep { StepNumber = 2, Name = "Director", ApproverType = ApproverType.Role, ApproverRole = "Director" },
            },
        };
        db.ApprovalWorkflows.Add(workflow);
        await db.SaveChangesAsync();
        var request = new ApprovalRequest
        {
            WorkflowId = workflow.Id, EntityType = "Expense", EntityId = expense.Id,
            CurrentStepNumber = 1, Status = ApprovalRequestStatus.Pending,
            RequestedById = OwnerUserId, RequestedAt = DateTimeOffset.UtcNow, Amount = expense.Amount,
        };
        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync();

        var published = 0;
        var publisher = new Mock<IMediator>();
        publisher.Setup(p => p.Publish(It.IsAny<ApprovalCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => published++)
            .Returns(Task.CompletedTask);

        var approveHandler = new ApproveRequestHandler(
            new ApprovalService(db, NullLogger<ApprovalService>.Instance), db, publisher.Object);

        await approveHandler.Handle(new ApproveRequestCommand(request.Id, ApproverUserId, "step 1 ok"), CancellationToken.None);

        published.Should().Be(0, "approving an intermediate step advances the workflow — it is not a terminal completion");
        (await db.ApprovalRequests.FindAsync(request.Id))!.Status.Should().Be(ApprovalRequestStatus.Pending);
        (await db.Expenses.FindAsync(expense.Id))!.Status.Should().Be(ExpenseStatus.Pending);
    }

    // ───────────────────────────── (d) reject through the workflow ─────────────────────────────

    [Fact] // F-26B-05 — a reject through the workflow sets the expense Rejected
    public async Task TerminalRejection_throughTheRejectHandler_setsExpenseRejected()
    {
        using var db = TestDbContextFactory.Create();
        var expense = await SeedExpenseAsync(db);
        var request = await SeedPendingRequestAsync(db, expense.Id, expense.Amount);

        var statusHandler = RealStatusHandler(db, []);
        var reactionHandler = new OnApprovalCompleted_UpdateExpenseStatus(
            ForwardingMediator(statusHandler), NullLogger<OnApprovalCompleted_UpdateExpenseStatus>.Instance);

        var publisher = new Mock<IMediator>();
        publisher.Setup(p => p.Publish(It.IsAny<ApprovalCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns<ApprovalCompletedEvent, CancellationToken>((evt, ct) => reactionHandler.Handle(evt, ct));

        var rejectHandler = new RejectRequestHandler(
            new ApprovalService(db, NullLogger<ApprovalService>.Instance), db, publisher.Object);

        await rejectHandler.Handle(new RejectRequestCommand(request.Id, ApproverUserId, "duplicate"), CancellationToken.None);

        (await db.ApprovalRequests.FindAsync(request.Id))!.Status.Should().Be(ApprovalRequestStatus.Rejected);
        (await db.Expenses.FindAsync(expense.Id))!.Status.Should().Be(ExpenseStatus.Rejected,
            "a workflow rejection must flow through the event chain to the expense status");
    }

    // ───────────────────────── reaction handler ignores non-Expense ─────────────────────────

    [Fact] // the reaction handler must ignore entity types it does not govern
    public async Task ReactionHandler_ignoresNonExpenseEntityTypes()
    {
        var mediator = new Mock<IMediator>();
        var handler = new OnApprovalCompleted_UpdateExpenseStatus(
            mediator.Object, NullLogger<OnApprovalCompleted_UpdateExpenseStatus>.Instance);

        await handler.Handle(
            new ApprovalCompletedEvent("PurchaseOrder", 99, Approved: true, DecidedById: ApproverUserId, Notes: null),
            CancellationToken.None);

        mediator.Verify(m => m.Send(It.IsAny<UpdateExpenseStatusCommand>(), It.IsAny<CancellationToken>()), Times.Never,
            "only Expense approvals translate to an expense status change");
    }
}
