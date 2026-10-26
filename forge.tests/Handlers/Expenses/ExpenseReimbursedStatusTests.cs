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
/// Reimbursed is reached only through the reimburse action, which posts the payout, and is terminal: the status
/// PATCH can neither set it nor move a reimbursed expense to another status.
/// </summary>
public class ExpenseReimbursedStatusTests
{
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

    private static async Task<(AppDbContext Db, Expense Expense)> SeedAsync(ExpenseStatus status)
    {
        var db = TestDbContextFactory.Create();
        var expense = new Expense
        {
            UserId = 42, Amount = 80m, Category = "Meals",
            Description = "Team lunch", Status = status, ApprovedBy = ApproverUserId,
            ExpenseDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        return (db, expense);
    }

    private static UpdateExpenseStatusHandler Handler(AppDbContext db)
        => new(new ExpenseRepository(db), HttpContextFor(ApproverUserId), new Mock<ISyncQueueRepository>().Object,
            new Mock<IAccountingProviderFactory>().Object, NullLogger<UpdateExpenseStatusHandler>.Instance, db: db);

    [Fact]
    public async Task Patching_an_approved_expense_to_reimbursed_is_refused()
    {
        var (db, expense) = await SeedAsync(ExpenseStatus.Approved);

        var act = () => Handler(db).Handle(
            new UpdateExpenseStatusCommand(expense.Id, new UpdateExpenseStatusRequestModel(ExpenseStatus.Reimbursed, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*reimburse action*");
        (await db.Expenses.AsNoTracking().SingleAsync()).Status.Should().Be(ExpenseStatus.Approved);
    }

    [Theory]
    [InlineData(ExpenseStatus.Approved)]
    [InlineData(ExpenseStatus.Pending)]
    [InlineData(ExpenseStatus.Rejected)]
    public async Task A_reimbursed_expense_keeps_its_status(ExpenseStatus requested)
    {
        var (db, expense) = await SeedAsync(ExpenseStatus.Reimbursed);

        var act = () => Handler(db).Handle(
            new UpdateExpenseStatusCommand(expense.Id, new UpdateExpenseStatusRequestModel(requested, "Changed my mind about this")),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been reimbursed*");
        (await db.Expenses.AsNoTracking().SingleAsync()).Status.Should().Be(ExpenseStatus.Reimbursed);
    }
}
