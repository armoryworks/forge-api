using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Expenses;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Expenses;

/// <summary>
/// The expense_self_approval setting governs a direct (PATCH) approval: with it off the submitter
/// cannot approve their own expense; with it on their approval is recorded as SelfApproved. Nobody
/// may mark someone else's expense SelfApproved, and SelfApproved cannot bypass a pending approval
/// workflow. The workflow-driven path (ActorUserId set) is unaffected.
/// </summary>
public class ExpenseSelfApprovalTests
{
    private const int OwnerUserId = 42;
    private const int ApproverUserId = 7;

    private static IHttpContextAccessor HttpContextFor(int userId)
        => new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test")),
            },
        };

    private static async Task<(AppDbContext Db, Expense Expense)> SeedAsync(bool? allowSelfApproval)
    {
        var db = TestDbContextFactory.Create();
        if (allowSelfApproval is bool allow)
            db.SystemSettings.Add(new SystemSetting { Key = "expense_self_approval", Value = allow ? "true" : "false" });
        var expense = new Expense
        {
            UserId = OwnerUserId, Amount = 80m, Category = "Meals",
            Description = "Team lunch", Status = ExpenseStatus.Pending,
            ExpenseDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        return (db, expense);
    }

    private static UpdateExpenseStatusHandler Handler(AppDbContext db, int actingUserId)
        => new(new ExpenseRepository(db), HttpContextFor(actingUserId), new Mock<ISyncQueueRepository>().Object,
            new Mock<IAccountingProviderFactory>().Object, NullLogger<UpdateExpenseStatusHandler>.Instance, db: db);

    private static UpdateExpenseStatusCommand Patch(int id, ExpenseStatus status)
        => new(id, new UpdateExpenseStatusRequestModel(status, null));

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Owner_CannotApproveOwnExpense_WhenSelfApprovalIsOff(bool? setting)
    {
        var (db, expense) = await SeedAsync(setting);

        foreach (var status in new[] { ExpenseStatus.Approved, ExpenseStatus.SelfApproved })
        {
            var act = () => Handler(db, OwnerUserId).Handle(Patch(expense.Id, status), CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*own expense*");
        }

        (await db.Expenses.AsNoTracking().SingleAsync()).Status.Should().Be(ExpenseStatus.Pending);
    }

    [Theory]
    [InlineData(ExpenseStatus.Approved)]
    [InlineData(ExpenseStatus.SelfApproved)]
    public async Task Owner_SelfApproves_WhenSelfApprovalIsOn(ExpenseStatus requested)
    {
        var (db, expense) = await SeedAsync(true);

        var result = await Handler(db, OwnerUserId).Handle(Patch(expense.Id, requested), CancellationToken.None);

        result.Status.Should().Be(ExpenseStatus.SelfApproved);
        var saved = await db.Expenses.AsNoTracking().SingleAsync();
        saved.Status.Should().Be(ExpenseStatus.SelfApproved);
        saved.ApprovedBy.Should().Be(OwnerUserId);
    }

    [Fact]
    public async Task AnotherApprover_Approves_AndCannotMarkItSelfApproved()
    {
        var (db, expense) = await SeedAsync(true);

        var selfApprove = () => Handler(db, ApproverUserId).Handle(Patch(expense.Id, ExpenseStatus.SelfApproved), CancellationToken.None);
        await selfApprove.Should().ThrowAsync<InvalidOperationException>().WithMessage("*submitted*");

        var result = await Handler(db, ApproverUserId).Handle(Patch(expense.Id, ExpenseStatus.Approved), CancellationToken.None);
        result.Status.Should().Be(ExpenseStatus.Approved);
    }

    [Fact]
    public async Task SelfApproved_CannotBypassAPendingWorkflow()
    {
        var (db, expense) = await SeedAsync(true);
        db.ApprovalRequests.Add(new ApprovalRequest
        {
            WorkflowId = 1, EntityType = "Expense", EntityId = expense.Id, CurrentStepNumber = 1,
            Status = ApprovalRequestStatus.Pending, RequestedById = OwnerUserId,
            RequestedAt = DateTimeOffset.UtcNow, Amount = expense.Amount,
        });
        await db.SaveChangesAsync();

        var act = () => Handler(db, OwnerUserId).Handle(Patch(expense.Id, ExpenseStatus.SelfApproved), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*approval workflow*");
    }

    [Fact]
    public async Task OwnerMayStillRejectOrSendBack_WhenSelfApprovalIsOff()
    {
        var (db, expense) = await SeedAsync(false);

        var result = await Handler(db, OwnerUserId).Handle(
            new UpdateExpenseStatusCommand(expense.Id, new UpdateExpenseStatusRequestModel(ExpenseStatus.Rejected, "Duplicate of an earlier claim")),
            CancellationToken.None);

        result.Status.Should().Be(ExpenseStatus.Rejected);
    }
}
