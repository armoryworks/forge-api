using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

using Forge.Api.Features.Leave;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Leave;

[Collection(PostgresCollection.Name)]
public sealed class LeaveBalanceAdjustmentPostgresTests(PostgresFixture fixture)
{
    private async Task<(int UserId, int PolicyId)> SeedAsync()
    {
        await using var seed = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new ApplicationUser
        {
            UserName = $"leave-{suffix}@example.com", Email = $"leave-{suffix}@example.com",
            FirstName = "Ben", LastName = $"Kelly{suffix}", IsActive = true,
        };
        var policy = new LeavePolicy { Name = $"PTO {suffix}", AccrualRatePerPayPeriod = 3.08m, IsPaidLeave = true };
        seed.Users.Add(user);
        seed.LeavePolicies.Add(policy);
        await seed.SaveChangesAsync();
        return (user.Id, policy.Id);
    }

    private static Task<LeaveBalanceAdjustmentResponseModel> AdjustAsync(
        AppDbContext db, int userId, int policyId, LeaveAdjustmentType type, decimal hours, string? reason = null)
        => new AdjustLeaveBalanceHandler(db).Handle(new AdjustLeaveBalanceCommand(userId,
            new AdjustLeaveBalanceRequestModel(policyId, type, hours, new DateOnly(2026, 10, 18), reason)), CancellationToken.None);

    [Fact]
    public async Task Opening_adjustment_and_an_approved_request_net_on_the_real_schema()
    {
        var (userId, policyId) = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            await AdjustAsync(db, userId, policyId, LeaveAdjustmentType.Opening, 40m);
            await AdjustAsync(db, userId, policyId, LeaveAdjustmentType.Adjustment, -2.5m, "Payroll correction");
        }

        await using (var db = fixture.CreateContext())
        {
            var request = await new CreateLeaveRequestHandler(db).Handle(new CreateLeaveRequestCommand(
                new CreateLeaveRequestModel(policyId, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2), 8m, null), userId),
                CancellationToken.None);
            var approver = await SeedAsync();
            await new DecideLeaveRequestHandler(db, new SystemClock())
                .Handle(new DecideLeaveRequestCommand(request.Id, true, approver.UserId), CancellationToken.None);
        }

        await using var verify = fixture.CreateContext();
        var balance = await verify.LeaveBalances.AsNoTracking().SingleAsync(b => b.UserId == userId && b.PolicyId == policyId);
        balance.Balance.Should().Be(29.5m);
        balance.UsedThisYear.Should().Be(8m);
        var ledger = await verify.LeaveBalanceAdjustments.AsNoTracking()
            .Where(a => a.UserId == userId).OrderBy(a => a.Id).Select(a => a.BalanceAfter).ToListAsync();
        ledger.Should().Equal(40m, 37.5m);

        var all = await new GetAllLeaveBalancesHandler(verify).Handle(new GetAllLeaveBalancesQuery(policyId), CancellationToken.None);
        all.Should().ContainSingle().Which.UserName.Should().StartWith("Kelly");
        var history = await new GetLeaveBalanceAdjustmentsHandler(verify, TestCaller.As(userId))
            .Handle(new GetLeaveBalanceAdjustmentsQuery(userId), CancellationToken.None);
        history.Select(h => h.Type).Should().Equal(LeaveAdjustmentType.Adjustment, LeaveAdjustmentType.Opening);
    }

    [Fact]
    public async Task The_database_holds_one_opening_balance_per_employee_and_policy()
    {
        var (userId, policyId) = await SeedAsync();
        await using var db = fixture.CreateContext();
        db.LeaveBalanceAdjustments.AddRange(
            new LeaveBalanceAdjustment { UserId = userId, PolicyId = policyId, AdjustmentType = LeaveAdjustmentType.Opening, Hours = 8m, BalanceAfter = 8m, EffectiveDate = new DateOnly(2026, 10, 1) },
            new LeaveBalanceAdjustment { UserId = userId, PolicyId = policyId, AdjustmentType = LeaveAdjustmentType.Opening, Hours = 4m, BalanceAfter = 12m, EffectiveDate = new DateOnly(2026, 10, 2) });

        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which.ConstraintName.Should().Be("ux_leave_balance_adjustments_opening");
    }

    [Fact]
    public async Task The_database_refuses_a_zero_hour_adjustment()
    {
        var (userId, policyId) = await SeedAsync();
        await using var db = fixture.CreateContext();
        db.LeaveBalanceAdjustments.Add(new LeaveBalanceAdjustment
        {
            UserId = userId, PolicyId = policyId, AdjustmentType = LeaveAdjustmentType.Adjustment,
            Hours = 0m, BalanceAfter = 0m, EffectiveDate = new DateOnly(2026, 10, 1), Reason = "none",
        });

        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which.ConstraintName.Should().Be("ck_leave_balance_adjustments_hours");
    }
}
