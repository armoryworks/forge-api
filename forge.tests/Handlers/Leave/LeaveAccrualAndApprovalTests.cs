using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Leave;
using Forge.Api.Features.TimeTracking;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Leave;

public class LeaveAccrualAndApprovalTests
{
    private static readonly DateTimeOffset MondayMorningMountain = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);
    private readonly TimerTestHarness _h = new();
    private LeavePolicy _pto = null!;

    private async Task SetUpAsync(decimal? maxBalance = 120m, int waitingDays = 0)
    {
        _h.Now = MondayMorningMountain;
        _h.Db.WorkingCalendars.Add(new WorkingCalendar { Name = "Plant", TimeZone = "America/Denver", IsDefault = true });
        _h.Db.SystemSettings.AddRange(
            new SystemSetting { Key = "pay_period_type", Value = "weekly" },
            new SystemSetting { Key = "pay_period_anchor", Value = "2026-10-12T00:00:00Z" });
        _pto = new LeavePolicy
        {
            Name = "PTO", AccrualRatePerPayPeriod = 3.08m, MaxBalance = maxBalance, CarryOverLimit = 40m,
            AccrueFromHireDate = true, WaitingPeriodDays = waitingDays, IsPaidLeave = true,
        };
        _h.Db.LeavePolicies.Add(_pto);
        await _h.Db.SaveChangesAsync();
    }

    private async Task<int> AddEmployeeAsync(DateTimeOffset? hired)
    {
        var user = await _h.AddUserAsync();
        _h.Db.EmployeeProfiles.Add(new EmployeeProfile { UserId = user.Id, StartDate = hired });
        await _h.Db.SaveChangesAsync();
        return user.Id;
    }

    private Task LockAsync(int year, int month, int day)
        => new LockPayPeriodHandler(_h.Db, _h.Clock.Object).Handle(
            new LockPayPeriodCommand(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);

    private Task<LeaveBalance?> BalanceAsync(int userId)
        => _h.Db.LeaveBalances.AsNoTracking().SingleOrDefaultAsync(b => b.UserId == userId && b.PolicyId == _pto.Id);

    private async Task SetBalanceAsync(int userId, decimal hours, DateTimeOffset? accruedThrough = null)
    {
        _h.Db.LeaveBalances.Add(new LeaveBalance
        {
            UserId = userId, PolicyId = _pto.Id, Balance = hours, LastAccrualDate = accruedThrough ?? MondayMorningMountain,
        });
        await _h.Db.SaveChangesAsync();
    }

    private Task<LeaveRequestResponseModel> RequestAsync(int userId, decimal hours)
        => new CreateLeaveRequestHandler(_h.Db).Handle(new CreateLeaveRequestCommand(
            new CreateLeaveRequestModel(_pto.Id, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2), hours, "Family event"), userId),
            CancellationToken.None);

    private Task DecideAsync(int requestId, bool approve, int deciderId)
        => new DecideLeaveRequestHandler(_h.Db, _h.Clock.Object)
            .Handle(new DecideLeaveRequestCommand(requestId, approve, deciderId), CancellationToken.None);

    [Fact]
    public async Task Locking_a_weekly_period_credits_one_period_of_pto_to_employees()
    {
        await SetUpAsync();
        var ben = await AddEmployeeAsync(new DateTimeOffset(2019, 9, 16, 0, 0, 0, TimeSpan.Zero));

        await LockAsync(2026, 10, 18);

        var balance = await BalanceAsync(ben);
        balance!.Balance.Should().Be(3.08m, "a long-tenured employee's first accrual is one period, not years of backfill");
        balance.AccruedThisYear.Should().Be(3.08m);
        balance.LastAccrualDate.Should().Be(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Relocking_the_same_period_does_not_accrue_twice_and_later_periods_each_accrue()
    {
        await SetUpAsync();
        var ben = await AddEmployeeAsync(new DateTimeOffset(2019, 9, 16, 0, 0, 0, TimeSpan.Zero));
        await LockAsync(2026, 10, 4);

        await LockAsync(2026, 10, 4);
        (await BalanceAsync(ben))!.Balance.Should().Be(3.08m);

        await LockAsync(2026, 10, 18);
        (await BalanceAsync(ben))!.Balance.Should().Be(9.24m, "the weeks ending 10/11 and 10/18 each credit once");
    }

    [Fact]
    public async Task Accrual_skips_users_without_an_employee_profile_or_start_date_and_honours_the_waiting_period()
    {
        await SetUpAsync(waitingDays: 90);
        var kiosk = await _h.AddUserAsync();
        var noStart = await AddEmployeeAsync(null);
        var newHire = await AddEmployeeAsync(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero));
        var veteran = await AddEmployeeAsync(new DateTimeOffset(2016, 6, 13, 0, 0, 0, TimeSpan.Zero));

        await LockAsync(2026, 10, 18);

        (await BalanceAsync(kiosk.Id)).Should().BeNull();
        (await BalanceAsync(noStart)).Should().BeNull();
        (await BalanceAsync(newHire)).Should().BeNull("a 90-day waiting period has not passed");
        (await BalanceAsync(veteran))!.Balance.Should().Be(3.08m);
    }

    [Fact]
    public async Task Accrual_stops_at_the_policy_maximum()
    {
        await SetUpAsync(maxBalance: 40m);
        var ben = await AddEmployeeAsync(new DateTimeOffset(2019, 9, 16, 0, 0, 0, TimeSpan.Zero));
        await SetBalanceAsync(ben, 39m, new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero));

        await LockAsync(2026, 10, 18);

        var balance = await BalanceAsync(ben);
        balance!.Balance.Should().Be(40m, "two weeks would add 6.16 h but the cap is 40");
        balance.AccruedThisYear.Should().Be(1m);
    }

    [Fact]
    public async Task A_request_larger_than_the_balance_is_refused_and_approval_rechecks_it()
    {
        await SetUpAsync();
        var ben = await AddEmployeeAsync(new DateTimeOffset(2019, 9, 16, 0, 0, 0, TimeSpan.Zero));
        var lena = await AddEmployeeAsync(new DateTimeOffset(2016, 6, 13, 0, 0, 0, TimeSpan.Zero));
        await SetBalanceAsync(ben, 32m);

        await FluentActions.Awaiting(() => RequestAsync(ben, 200m)).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*200 h requested, 32 h available*");

        var first = await RequestAsync(ben, 24m);
        await FluentActions.Awaiting(() => RequestAsync(ben, 16m)).Should().ThrowAsync<InvalidOperationException>(
            "24 h is already pending against the 32 h balance");

        _h.Db.LeaveRequests.Add(new LeaveRequest { UserId = ben, PolicyId = _pto.Id, Hours = 16m, StartDate = new DateOnly(2026, 12, 1), EndDate = new DateOnly(2026, 12, 2) });
        await _h.Db.SaveChangesAsync();
        await DecideAsync(first.Id, true, lena);
        var second = await _h.Db.LeaveRequests.AsNoTracking().SingleAsync(r => r.UserId == ben && r.Hours == 16m);
        await FluentActions.Awaiting(() => DecideAsync(second.Id, true, lena)).Should().ThrowAsync<InvalidOperationException>();

        (await BalanceAsync(ben))!.Balance.Should().Be(8m);
    }

    [Fact]
    public async Task A_manager_cannot_approve_her_own_request_but_can_deny_it()
    {
        await SetUpAsync();
        var lena = await AddEmployeeAsync(new DateTimeOffset(2016, 6, 13, 0, 0, 0, TimeSpan.Zero));
        await SetBalanceAsync(lena, 10m);
        var own = await RequestAsync(lena, 2m);

        await FluentActions.Awaiting(() => DecideAsync(own.Id, true, lena)).Should().ThrowAsync<ForbiddenException>();
        (await _h.Db.LeaveRequests.AsNoTracking().SingleAsync(r => r.Id == own.Id)).Status.Should().Be(LeaveRequestStatus.Pending);

        await DecideAsync(own.Id, false, lena);
        (await _h.Db.LeaveRequests.AsNoTracking().SingleAsync(r => r.Id == own.Id)).Status.Should().Be(LeaveRequestStatus.Denied);
    }

    [Fact]
    public async Task Unpaid_leave_is_not_limited_by_a_balance()
    {
        await SetUpAsync();
        var unpaid = new LeavePolicy { Name = "Unpaid", AccrualRatePerPayPeriod = 0m, IsPaidLeave = false };
        _h.Db.LeavePolicies.Add(unpaid);
        await _h.Db.SaveChangesAsync();
        var ben = await AddEmployeeAsync(new DateTimeOffset(2019, 9, 16, 0, 0, 0, TimeSpan.Zero));

        var request = await new CreateLeaveRequestHandler(_h.Db).Handle(new CreateLeaveRequestCommand(
            new CreateLeaveRequestModel(unpaid.Id, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 20), 120m, null), ben),
            CancellationToken.None);

        request.Hours.Should().Be(120m);
    }

    [Fact]
    public async Task Balances_and_requests_are_the_callers_own_unless_supervisor()
    {
        await SetUpAsync();
        var ben = await AddEmployeeAsync(new DateTimeOffset(2019, 9, 16, 0, 0, 0, TimeSpan.Zero));
        var rosa = await AddEmployeeAsync(new DateTimeOffset(2021, 4, 5, 0, 0, 0, TimeSpan.Zero));
        var lena = await AddEmployeeAsync(new DateTimeOffset(2016, 6, 13, 0, 0, 0, TimeSpan.Zero));
        await SetBalanceAsync(ben, 32m);
        await SetBalanceAsync(rosa, 16m);
        await RequestAsync(ben, 8m);
        await RequestAsync(rosa, 8m);

        Task<List<LeaveBalanceResponseModel>> Balances(int caller, string[] roles, int userId)
            => new GetLeaveBalancesHandler(_h.Db, TestCaller.As(caller, roles))
                .Handle(new GetLeaveBalancesQuery(userId), CancellationToken.None);
        Task<List<LeaveRequestResponseModel>> Requests(int caller, string[] roles, int? userId)
            => new GetLeaveRequestsHandler(_h.Db, TestCaller.As(caller, roles))
                .Handle(new GetLeaveRequestsQuery(userId), CancellationToken.None);

        await FluentActions.Awaiting(() => Balances(ben, [], rosa)).Should().ThrowAsync<ForbiddenException>();
        (await Balances(ben, [], ben)).Single().Balance.Should().Be(32m);
        (await Balances(lena, ["Manager"], ben)).Single().Balance.Should().Be(32m);

        (await Requests(ben, [], null)).Should().OnlyContain(r => r.UserId == ben);
        await FluentActions.Awaiting(() => Requests(ben, [], rosa)).Should().ThrowAsync<ForbiddenException>();
        (await Requests(lena, ["Manager"], null)).Select(r => r.UserId).Should().BeEquivalentTo([ben, rosa]);
    }

    [Theory]
    [InlineData("OfficeManager")]
    [InlineData("Controller")]
    public async Task Payroll_roles_read_everyones_leave_but_cannot_decide_it(string role)
    {
        await SetUpAsync();
        var ben = await AddEmployeeAsync(new DateTimeOffset(2019, 9, 16, 0, 0, 0, TimeSpan.Zero));
        var rosa = await AddEmployeeAsync(new DateTimeOffset(2021, 4, 5, 0, 0, 0, TimeSpan.Zero));
        var lena = await AddEmployeeAsync(new DateTimeOffset(2016, 6, 13, 0, 0, 0, TimeSpan.Zero));
        await SetBalanceAsync(ben, 32m);
        await SetBalanceAsync(rosa, 16m);
        await RequestAsync(ben, 8m);
        await RequestAsync(rosa, 8m);

        (await new GetLeaveBalancesHandler(_h.Db, TestCaller.As(lena, [role]))
            .Handle(new GetLeaveBalancesQuery(rosa), CancellationToken.None)).Single().Balance.Should().Be(16m);
        (await new GetLeaveRequestsHandler(_h.Db, TestCaller.As(lena, [role]))
            .Handle(new GetLeaveRequestsQuery(), CancellationToken.None)).Select(r => r.UserId).Should().BeEquivalentTo([ben, rosa]);
        (await new GetLeaveRequestsHandler(_h.Db, TestCaller.As(lena, [role]))
            .Handle(new GetLeaveRequestsQuery(ben), CancellationToken.None)).Should().ContainSingle().Which.UserId.Should().Be(ben);
    }

    [Fact]
    public async Task A_production_worker_still_reads_only_their_own_leave()
    {
        await SetUpAsync();
        var ben = await AddEmployeeAsync(new DateTimeOffset(2019, 9, 16, 0, 0, 0, TimeSpan.Zero));
        var rosa = await AddEmployeeAsync(new DateTimeOffset(2021, 4, 5, 0, 0, 0, TimeSpan.Zero));
        await SetBalanceAsync(ben, 32m);
        await SetBalanceAsync(rosa, 16m);
        await RequestAsync(ben, 8m);
        await RequestAsync(rosa, 8m);

        await FluentActions.Awaiting(() => new GetLeaveBalancesHandler(_h.Db, TestCaller.As(ben, ["ProductionWorker"]))
            .Handle(new GetLeaveBalancesQuery(rosa), CancellationToken.None)).Should().ThrowAsync<ForbiddenException>();
        (await new GetLeaveRequestsHandler(_h.Db, TestCaller.As(ben, ["ProductionWorker"]))
            .Handle(new GetLeaveRequestsQuery(), CancellationToken.None)).Should().OnlyContain(r => r.UserId == ben);
    }
}
