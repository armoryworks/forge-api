using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Leave;
using Forge.Api.Features.TimeTracking;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Leave;

public class LeaveBalanceAdjustmentTests
{
    private static readonly DateTimeOffset MondayMorningMountain = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);
    private readonly TimerTestHarness _h = new();
    private LeavePolicy _pto = null!;

    private async Task SetUpAsync()
    {
        _h.Now = MondayMorningMountain;
        _h.Db.WorkingCalendars.Add(new WorkingCalendar { Name = "Plant", TimeZone = "America/Denver", IsDefault = true });
        _h.Db.SystemSettings.AddRange(
            new SystemSetting { Key = "pay_period_type", Value = "weekly" },
            new SystemSetting { Key = "pay_period_anchor", Value = "2026-10-12T00:00:00Z" });
        _pto = new LeavePolicy
        {
            Name = "PTO", AccrualRatePerPayPeriod = 3.08m, MaxBalance = 120m, CarryOverLimit = 40m,
            AccrueFromHireDate = true, WaitingPeriodDays = 0, IsPaidLeave = true,
        };
        _h.Db.LeavePolicies.Add(_pto);
        await _h.Db.SaveChangesAsync();
    }

    private async Task<int> AddInRoleAsync(string role)
    {
        var user = await _h.AddUserAsync();
        var row = await _h.Db.Roles.FirstOrDefaultAsync(r => r.Name == role);
        if (row is null)
        {
            row = new IdentityRole<int> { Name = role, NormalizedName = role.ToUpperInvariant() };
            _h.Db.Roles.Add(row);
            await _h.Db.SaveChangesAsync();
        }
        _h.Db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = row.Id });
        await _h.Db.SaveChangesAsync();
        return user.Id;
    }

    private Task<LeaveBalanceAdjustmentResponseModel> AdjustAsync(
        int userId, LeaveAdjustmentType type, decimal hours, string? reason = null, DateOnly? on = null)
        => new AdjustLeaveBalanceHandler(_h.Db).Handle(new AdjustLeaveBalanceCommand(userId,
            new AdjustLeaveBalanceRequestModel(_pto.Id, type, hours, on ?? new DateOnly(2026, 10, 18), reason)),
            CancellationToken.None);

    private Task<LeaveBalance?> BalanceAsync(int userId)
        => _h.Db.LeaveBalances.AsNoTracking().SingleOrDefaultAsync(b => b.UserId == userId && b.PolicyId == _pto.Id);

    [Fact]
    public async Task An_opening_balance_creates_the_balance_and_writes_a_ledger_row()
    {
        await SetUpAsync();
        var ben = (await _h.AddUserAsync()).Id;

        var result = await AdjustAsync(ben, LeaveAdjustmentType.Opening, 24.5m);

        result.BalanceAfter.Should().Be(24.5m);
        result.PolicyName.Should().Be("PTO");
        var balance = await BalanceAsync(ben);
        balance!.Balance.Should().Be(24.5m);
        balance.LastAccrualDate.Should().Be(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero),
            "accrual resumes with the first period that ends after the opening date");
        var row = await _h.Db.LeaveBalanceAdjustments.AsNoTracking().SingleAsync();
        (row.AdjustmentType, row.Hours, row.BalanceAfter).Should().Be((LeaveAdjustmentType.Opening, 24.5m, 24.5m));
        (await _h.Db.ActivityLogs.AnyAsync(a => a.Action == "leave-opening-balance" && a.EntityId == ben)).Should().BeTrue();
    }

    [Fact]
    public async Task A_second_opening_balance_is_refused()
    {
        await SetUpAsync();
        var ben = (await _h.AddUserAsync()).Id;
        await AdjustAsync(ben, LeaveAdjustmentType.Opening, 16m);

        var act = () => AdjustAsync(ben, LeaveAdjustmentType.Opening, 8m);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already has an opening PTO balance*");
        (await BalanceAsync(ben))!.Balance.Should().Be(16m);
    }

    [Fact]
    public async Task Adjustments_add_and_remove_hours_but_never_below_zero()
    {
        await SetUpAsync();
        var ben = (await _h.AddUserAsync()).Id;
        await AdjustAsync(ben, LeaveAdjustmentType.Opening, 10m);

        (await AdjustAsync(ben, LeaveAdjustmentType.Adjustment, 4m, "Comp day")).BalanceAfter.Should().Be(14m);
        (await AdjustAsync(ben, LeaveAdjustmentType.Adjustment, -6m, "Payroll correction")).BalanceAfter.Should().Be(8m);
        var act = () => AdjustAsync(ben, LeaveAdjustmentType.Adjustment, -9m, "Too much");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot go below zero*");
        (await BalanceAsync(ben))!.Balance.Should().Be(8m);
        (await _h.Db.LeaveBalanceAdjustments.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Accrual_after_an_opening_balance_credits_only_later_periods()
    {
        await SetUpAsync();
        var ben = (await _h.AddUserAsync()).Id;
        _h.Db.EmployeeProfiles.Add(new EmployeeProfile { UserId = ben, StartDate = new DateTimeOffset(2019, 9, 16, 0, 0, 0, TimeSpan.Zero) });
        await _h.Db.SaveChangesAsync();
        await AdjustAsync(ben, LeaveAdjustmentType.Opening, 40m, on: new DateOnly(2026, 10, 18));

        await new LockPayPeriodHandler(_h.Db, _h.Clock.Object).Handle(
            new LockPayPeriodCommand(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);
        (await BalanceAsync(ben))!.Balance.Should().Be(40m, "the opening balance already covers the period ending on its date");

        _h.Now = MondayMorningMountain.AddDays(7);
        await new LockPayPeriodHandler(_h.Db, _h.Clock.Object).Handle(
            new LockPayPeriodCommand(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);
        (await BalanceAsync(ben))!.Balance.Should().Be(43.08m);
    }

    [Fact]
    public void The_validator_requires_a_reason_for_an_adjustment_and_a_non_negative_opening()
    {
        var validator = new AdjustLeaveBalanceValidator();
        AdjustLeaveBalanceCommand Make(LeaveAdjustmentType type, decimal hours, string? reason)
            => new(5, new AdjustLeaveBalanceRequestModel(1, type, hours, new DateOnly(2026, 10, 1), reason));

        validator.Validate(Make(LeaveAdjustmentType.Adjustment, 4m, null)).IsValid.Should().BeFalse();
        validator.Validate(Make(LeaveAdjustmentType.Adjustment, 0m, "x")).IsValid.Should().BeFalse();
        validator.Validate(Make(LeaveAdjustmentType.Opening, -1m, null)).IsValid.Should().BeFalse();
        validator.Validate(Make(LeaveAdjustmentType.Opening, 0m, null)).IsValid.Should().BeTrue();
        validator.Validate(Make(LeaveAdjustmentType.Adjustment, -2m, "Correction")).IsValid.Should().BeTrue();
        validator.Validate(Make(LeaveAdjustmentType.Adjustment, 0.001m, "Rounds to nothing")).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Adjustment_history_is_the_callers_own_unless_a_payroll_reader()
    {
        await SetUpAsync();
        var ben = (await _h.AddUserAsync()).Id;
        var rosa = (await _h.AddUserAsync()).Id;
        await AdjustAsync(ben, LeaveAdjustmentType.Opening, 10m);
        await AdjustAsync(rosa, LeaveAdjustmentType.Opening, 20m);

        Task<List<LeaveBalanceAdjustmentResponseModel>> History(int caller, string[] roles, int userId)
            => new GetLeaveBalanceAdjustmentsHandler(_h.Db, TestCaller.As(caller, roles))
                .Handle(new GetLeaveBalanceAdjustmentsQuery(userId), CancellationToken.None);

        (await History(ben, [], ben)).Should().ContainSingle().Which.Hours.Should().Be(10m);
        await FluentActions.Awaiting(() => History(ben, ["ProductionWorker"], rosa)).Should().ThrowAsync<ForbiddenException>();
        (await History(ben, ["Controller"], rosa)).Should().ContainSingle().Which.Hours.Should().Be(20m);
        (await History(ben, ["OfficeManager"], rosa)).Should().ContainSingle();
    }

    [Fact]
    public async Task All_balances_list_carries_the_employee_name()
    {
        await SetUpAsync();
        var ben = (await _h.AddUserAsync()).Id;
        await AdjustAsync(ben, LeaveAdjustmentType.Opening, 12m);

        var rows = await new GetAllLeaveBalancesHandler(_h.Db).Handle(new GetAllLeaveBalancesQuery(), CancellationToken.None);

        rows.Should().ContainSingle().Which.Should().Match<LeaveBalanceResponseModel>(r =>
            r.UserId == ben && r.Balance == 12m && r.UserName == "Machinist, Pat");
    }

    [Fact]
    public async Task Updating_a_policy_changes_its_terms_and_logs_one_row()
    {
        await SetUpAsync();

        var result = await new UpdateLeavePolicyHandler(_h.Db).Handle(new UpdateLeavePolicyCommand(_pto.Id,
            new UpdateLeavePolicyRequestModel("PTO", 4m, 160m, 40m, true, 30, true, true)), CancellationToken.None);

        result.AccrualRatePerPayPeriod.Should().Be(4m);
        result.MaxBalance.Should().Be(160m);
        result.WaitingPeriodDays.Should().Be(30);
        var log = await _h.Db.ActivityLogs.SingleAsync(a => a.EntityType == "LeavePolicy" && a.EntityId == _pto.Id && a.Action == "updated");
        log.Description.Should().StartWith("Updated 3 fields");
    }

    [Fact]
    public async Task A_policy_cannot_take_another_policys_name()
    {
        await SetUpAsync();
        _h.Db.LeavePolicies.Add(new LeavePolicy { Name = "Sick", AccrualRatePerPayPeriod = 1m });
        await _h.Db.SaveChangesAsync();

        var act = () => new UpdateLeavePolicyHandler(_h.Db).Handle(new UpdateLeavePolicyCommand(_pto.Id,
            new UpdateLeavePolicyRequestModel("Sick", 4m, null, null, true, null, true, true)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Employees_cancel_their_own_pending_requests_only()
    {
        await SetUpAsync();
        var ben = (await _h.AddUserAsync()).Id;
        var rosa = (await _h.AddUserAsync()).Id;
        await AdjustAsync(ben, LeaveAdjustmentType.Opening, 40m);
        var request = await new CreateLeaveRequestHandler(_h.Db).Handle(new CreateLeaveRequestCommand(
            new CreateLeaveRequestModel(_pto.Id, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2), 8m, "Dentist"), ben),
            CancellationToken.None);

        await FluentActions.Awaiting(() => new CancelLeaveRequestHandler(_h.Db, TestCaller.As(rosa, []))
            .Handle(new CancelLeaveRequestCommand(request.Id), CancellationToken.None)).Should().ThrowAsync<ForbiddenException>();

        await new CancelLeaveRequestHandler(_h.Db, TestCaller.As(ben, []))
            .Handle(new CancelLeaveRequestCommand(request.Id), CancellationToken.None);

        (await _h.Db.LeaveRequests.AsNoTracking().SingleAsync()).Status.Should().Be(LeaveRequestStatus.Cancelled);
        await FluentActions.Awaiting(() => new CancelLeaveRequestHandler(_h.Db, TestCaller.As(ben, []))
            .Handle(new CancelLeaveRequestCommand(request.Id), CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Approvers_hear_about_new_requests_and_the_requester_hears_the_decision()
    {
        await SetUpAsync();
        var ben = (await _h.AddUserAsync()).Id;
        var lena = await AddInRoleAsync("Manager");
        var ada = await AddInRoleAsync("Admin");
        var pat = await AddInRoleAsync("ProductionWorker");
        await AdjustAsync(ben, LeaveAdjustmentType.Opening, 40m);

        var request = await new CreateLeaveRequestHandler(_h.Db).Handle(new CreateLeaveRequestCommand(
            new CreateLeaveRequestModel(_pto.Id, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3), 16m, null), ben),
            CancellationToken.None);

        var toApprovers = await _h.Db.Notifications.AsNoTracking()
            .Where(n => n.EntityType == LeaveNotifications.ApproverEntityType).Select(n => n.UserId).ToListAsync();
        toApprovers.Should().BeEquivalentTo([lena, ada]);
        toApprovers.Should().NotContain(pat);

        await new DecideLeaveRequestHandler(_h.Db, _h.Clock.Object)
            .Handle(new DecideLeaveRequestCommand(request.Id, false, lena, "Short-staffed"), CancellationToken.None);

        var decision = await _h.Db.Notifications.AsNoTracking()
            .SingleAsync(n => n.EntityType == LeaveNotifications.RequesterEntityType);
        decision.UserId.Should().Be(ben);
        decision.Title.Should().Be("Leave denied");
        decision.Message.Should().Contain("Short-staffed");
    }

    [Fact]
    public async Task Admins_get_one_notice_per_employee_accruing_without_a_hire_date()
    {
        await SetUpAsync();
        _h.Db.LeavePolicies.Add(new LeavePolicy { Name = "Sick", AccrualRatePerPayPeriod = 1m, IsPaidLeave = true });
        await _h.Db.SaveChangesAsync();
        var ada = await AddInRoleAsync("Admin");
        var ben = await _h.AddUserAsync();
        ben.CreatedAt = new DateTimeOffset(2025, 3, 3, 0, 0, 0, TimeSpan.Zero);
        _h.Db.EmployeeProfiles.Add(new EmployeeProfile { UserId = ada, StartDate = new DateTimeOffset(2020, 1, 6, 0, 0, 0, TimeSpan.Zero) });
        await _h.Db.SaveChangesAsync();

        await new LockPayPeriodHandler(_h.Db, _h.Clock.Object).Handle(
            new LockPayPeriodCommand(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);
        _h.Db.LeavePolicies.Add(new LeavePolicy { Name = "Floating holiday", AccrualRatePerPayPeriod = 0.5m, IsPaidLeave = true });
        await _h.Db.SaveChangesAsync();
        _h.Now = MondayMorningMountain.AddDays(7);
        await new LockPayPeriodHandler(_h.Db, _h.Clock.Object).Handle(
            new LockPayPeriodCommand(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);

        var notices = await _h.Db.Notifications.AsNoTracking()
            .Where(n => n.Source == LeaveAccrual.NoHireDateSource).ToListAsync();
        notices.Should().ContainSingle();
        notices[0].UserId.Should().Be(ada);
        notices[0].EntityId.Should().Be(ben.Id);
        notices[0].Message.Should().Contain("accruing leave from 03/03/2025");
    }
}
