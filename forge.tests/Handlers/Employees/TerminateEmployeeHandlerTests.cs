using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Admin;
using Forge.Api.Features.Employees;
using Forge.Api.Features.Notifications;
using Forge.Api.Jobs;
using Forge.Api.Middleware;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Employees;

public class TerminateEmployeeHandlerTests
{
    private readonly TimerTestHarness _h = new();
    private readonly Mock<ISessionStore> _sessions = new();
    private readonly Mock<ISystemAuditWriter> _audit = new();
    private int _adminId;

    public TerminateEmployeeHandlerTests()
    {
        _h.ClockEventTypes.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClockEventTypeDefinition>
            {
                new("ClockIn", "Clock In", "In", "ClockOut", "work", true, true, "login", "#22c55e"),
                new("ClockOut", "Clock Out", "Out", "ClockIn", "work", false, false, "logout", "#ef4444"),
            });
        foreach (var (code, label) in TerminationReasons.Seeded)
            _h.Db.ReferenceData.Add(new ReferenceData { GroupCode = TerminationReasons.GroupCode, Code = code, Label = label });
        _h.Db.SaveChanges();
    }

    private DateOnly Today => DateOnly.FromDateTime(_h.Now.UtcDateTime);

    private EmployeeTermination Termination()
        => new(
            _h.Db,
            new UserDeactivation(_h.Db, _h.Mediator.Object, _h.ClockEventTypes.Object, _h.Clock.Object, _sessions.Object),
            _audit.Object,
            _h.Mediator.Object,
            _h.Clock.Object);

    private Task<EmployeeTerminationResponseModel> Terminate(
        int id, DateOnly date, string reason = "resignation", int? caller = null, bool admin = true)
        => new TerminateEmployeeHandler(_h.Db, Termination(), _h.Clock.Object).Handle(
            new TerminateEmployeeCommand(id, EmployeeKinds.User,
                new TerminateEmployeeRequestModel(date, reason, "Two weeks' notice", true, true),
                caller ?? _adminId, admin),
            CancellationToken.None);

    private async Task<int> AddRoleAsync(int userId, string role)
    {
        var existing = await _h.Db.Roles.FirstOrDefaultAsync(r => r.Name == role);
        if (existing is null)
        {
            existing = new IdentityRole<int> { Name = role, NormalizedName = role.ToUpperInvariant() };
            _h.Db.Roles.Add(existing);
            await _h.Db.SaveChangesAsync();
        }
        _h.Db.UserRoles.Add(new IdentityUserRole<int> { UserId = userId, RoleId = existing.Id });
        await _h.Db.SaveChangesAsync();
        return existing.Id;
    }

    private async Task<ApplicationUser> SeedWorkerAsync()
    {
        var admin = await _h.AddUserAsync();
        _adminId = admin.Id;
        await AddRoleAsync(admin.Id, "Admin");

        var worker = await _h.AddUserAsync();
        worker.PinHash = "hash";
        worker.EmployeeBarcode = "EMP-0042";
        _h.Db.UserScanIdentifiers.Add(new UserScanIdentifier { UserId = worker.Id, IdentifierType = "rfid", IdentifierValue = "04A1B2" });
        _h.Db.UserMfaDevices.Add(new UserMfaDevice { UserId = worker.Id, DeviceType = MfaDeviceType.WebAuthn, CredentialId = "cred" });
        _h.Db.UserMfaDevices.Add(new UserMfaDevice { UserId = worker.Id, DeviceType = MfaDeviceType.Totp });
        _h.Db.SystemApiKeys.Add(new SystemApiKey { UserId = worker.Id, Name = "sync", KeyHash = "h", KeyPrefix = "p" });
        _h.Db.EmployeeProfiles.Add(new EmployeeProfile { UserId = worker.Id, HourlyRate = 20m });
        var policy = new LeavePolicy { Name = "PTO", AccrualRatePerPayPeriod = 4m, IsPaidLeave = true };
        _h.Db.LeavePolicies.Add(policy);
        await _h.Db.SaveChangesAsync();
        _h.Db.LeaveBalances.Add(new LeaveBalance { UserId = worker.Id, PolicyId = policy.Id, Balance = 12m });
        _h.Db.LeaveRequests.Add(new LeaveRequest { UserId = worker.Id, PolicyId = policy.Id, StartDate = Today.AddDays(20), EndDate = Today.AddDays(21), Hours = 16 });
        await _h.Db.SaveChangesAsync();
        return worker;
    }

    [Fact]
    public async Task Terminating_today_deactivates_and_withdraws_every_credential()
    {
        var worker = await SeedWorkerAsync();
        var job = await _h.AddJobAsync("J-2001");
        job.AssigneeId = worker.Id;
        await _h.Db.SaveChangesAsync();

        var result = await Terminate(worker.Id, Today);

        result.AppliedAt.Should().Be(_h.Now);
        result.Effects!.KioskCredentialsCleared.Should().BeTrue();
        result.Effects.ScanIdentifiersDeactivated.Should().Be(1);
        result.Effects.PasskeysRemoved.Should().Be(1);
        result.Effects.ApiKeysRevoked.Should().Be(1);
        result.Effects.LeaveRequestsCancelled.Should().Be(1);
        result.Effects.UnassignedJobCount.Should().Be(1);

        worker.IsActive.Should().BeFalse();
        worker.PinHash.Should().BeNull();
        worker.EmployeeBarcode.Should().BeNull();
        _h.Db.UserScanIdentifiers.Single(s => s.UserId == worker.Id).IsActive.Should().BeFalse();
        _h.Db.UserMfaDevices.Count(d => d.UserId == worker.Id).Should().Be(1);
        _h.Db.SystemApiKeys.Single(k => k.UserId == worker.Id).IsActive.Should().BeFalse();
        _h.Db.LeaveRequests.Single(r => r.UserId == worker.Id).Status.Should().Be(LeaveRequestStatus.Cancelled);
        job.AssigneeId.Should().BeNull();
        _sessions.Verify(s => s.RevokeAllUserSessionsAsync(worker.Id, It.IsAny<CancellationToken>()), Times.Once);
        _h.Mediator.Verify(m => m.Send(
            It.Is<CreateNotificationCommand>(c => c.Data.UserId == _adminId && c.Data.Message.Contains("J-2001")),
            It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.WriteAsync(EmployeeTermination.AuditAction, _adminId, "EmployeeProfile", It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_future_date_is_scheduled_and_the_job_applies_it_on_the_day()
    {
        var worker = await SeedWorkerAsync();

        var result = await Terminate(worker.Id, Today.AddDays(3));

        result.AppliedAt.Should().BeNull();
        worker.IsActive.Should().BeTrue();
        worker.PinHash.Should().Be("hash");

        var job = new ApplyScheduledTerminationsJob(_h.Db, Termination(), _h.Clock.Object, NullLogger<ApplyScheduledTerminationsJob>.Instance);
        await job.ApplyDueAsync();
        worker.IsActive.Should().BeTrue();

        _h.Now = _h.Now.AddDays(3);
        await job.ApplyDueAsync();

        var profile = await _h.Db.EmployeeProfiles.SingleAsync(p => p.UserId == worker.Id);
        profile.TerminationAppliedAt.Should().Be(_h.Now);
        worker.IsActive.Should().BeFalse();
        worker.EmployeeBarcode.Should().BeNull();
    }

    [Fact]
    public async Task Rehire_reactivates_and_clears_the_termination_but_keeps_history()
    {
        var worker = await SeedWorkerAsync();
        await Terminate(worker.Id, Today);

        await new RehireEmployeeHandler(_h.Db, _audit.Object, _h.Clock.Object).Handle(
            new RehireEmployeeCommand(worker.Id, EmployeeKinds.User, new RehireEmployeeRequestModel(Today.AddDays(30)), _adminId, true),
            CancellationToken.None);

        var profile = await _h.Db.EmployeeProfiles.SingleAsync(p => p.UserId == worker.Id);
        worker.IsActive.Should().BeTrue();
        profile.TerminationDate.Should().BeNull();
        profile.TerminationAppliedAt.Should().BeNull();
        profile.StartDate!.Value.Date.Should().Be(Today.AddDays(30).ToDateTime(TimeOnly.MinValue));
        _h.Db.ActivityLogs.Where(a => a.EntityType == "Employee" && a.EntityId == worker.Id).Select(a => a.Action)
            .Should().Contain(["terminated", "rehired"]);
        _audit.Verify(a => a.WriteAsync(EmployeeTermination.RehireAuditAction, _adminId, "EmployeeProfile", It.IsAny<int?>(),
            It.Is<string?>(d => d!.Contains("resignation")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_already_terminated_employee_cannot_be_terminated_again()
    {
        var worker = await SeedWorkerAsync();
        await Terminate(worker.Id, Today);

        await FluentActions.Awaiting(() => Terminate(worker.Id, Today)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task An_unknown_reason_is_refused()
    {
        var worker = await SeedWorkerAsync();

        await FluentActions.Awaiting(() => Terminate(worker.Id, Today, reason: "vanished")).Should().ThrowAsync<ValidationException>();
        worker.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task The_last_active_admin_cannot_be_terminated()
    {
        var worker = await SeedWorkerAsync();
        var other = await _h.AddUserAsync();
        await AddRoleAsync(other.Id, "Manager");
        _h.Db.Users.Single(u => u.Id == _adminId).IsActive = true;
        await _h.Db.SaveChangesAsync();

        await FluentActions.Awaiting(() => Terminate(_adminId, Today, caller: other.Id, admin: true))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(UserDeactivation.LastAdminMessage);
        worker.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Nobody_terminates_their_own_record()
    {
        await SeedWorkerAsync();

        await FluentActions.Awaiting(() => Terminate(_adminId, Today, caller: _adminId))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_manager_cannot_terminate_someone_outside_their_team()
    {
        var worker = await SeedWorkerAsync();
        var manager = await _h.AddUserAsync();
        manager.TeamId = 7;
        worker.TeamId = 8;
        await _h.Db.SaveChangesAsync();

        await FluentActions.Awaiting(() => Terminate(worker.Id, Today, caller: manager.Id, admin: false))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task A_manager_cannot_terminate_an_admin_on_their_team()
    {
        var worker = await SeedWorkerAsync();
        var manager = await _h.AddUserAsync();
        await AddRoleAsync(manager.Id, "Manager");
        var otherAdmin = await _h.AddUserAsync();
        await AddRoleAsync(otherAdmin.Id, "Admin");
        manager.TeamId = 7;
        otherAdmin.TeamId = 7;
        worker.TeamId = 7;
        await _h.Db.SaveChangesAsync();

        await FluentActions.Awaiting(() => Terminate(otherAdmin.Id, Today, caller: manager.Id, admin: false))
            .Should().ThrowAsync<ForbiddenException>();
        otherAdmin.IsActive.Should().BeTrue();

        await Terminate(worker.Id, Today, caller: manager.Id, admin: false);
        worker.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Approved_leave_after_the_last_day_goes_back_onto_the_balance()
    {
        var worker = await SeedWorkerAsync();
        var policyId = _h.Db.LeavePolicies.Single().Id;
        var balance = _h.Db.LeaveBalances.Single(b => b.UserId == worker.Id);
        balance.Balance = 4m;
        balance.UsedThisYear = 8m;
        _h.Db.LeaveRequests.Add(new LeaveRequest
        {
            UserId = worker.Id, PolicyId = policyId, StartDate = Today.AddDays(5), EndDate = Today.AddDays(5),
            Hours = 8, Status = LeaveRequestStatus.Approved,
        });
        _h.Db.LeaveRequests.Add(new LeaveRequest
        {
            UserId = worker.Id, PolicyId = policyId, StartDate = Today.AddDays(-3), EndDate = Today.AddDays(-3),
            Hours = 8, Status = LeaveRequestStatus.Approved,
        });
        await _h.Db.SaveChangesAsync();

        var result = await Terminate(worker.Id, Today);

        result.Effects!.LeaveRequestsCancelled.Should().Be(2);
        balance.Balance.Should().Be(12m);
        balance.UsedThisYear.Should().Be(0m);
        _h.Db.LeaveRequests.Single(r => r.StartDate == Today.AddDays(-3)).Status.Should().Be(LeaveRequestStatus.Approved);
    }

    [Fact]
    public async Task Final_pay_summary_values_paid_leave_and_processing_zeroes_it()
    {
        var worker = await SeedWorkerAsync();
        _h.Db.TimeEntries.Add(new TimeEntry { UserId = worker.Id, Date = Today, DurationMinutes = 450 });
        await _h.Db.SaveChangesAsync();
        await Terminate(worker.Id, Today);

        var summary = await new GetFinalPaySummaryHandler(_h.Db, _h.Clock.Object).Handle(
            new GetFinalPaySummaryQuery(worker.Id, EmployeeKinds.User, _adminId, true), CancellationToken.None);

        summary.PtoPayoutHours.Should().Be(12m);
        summary.PtoPayoutAmount.Should().Be(240m);
        summary.HoursSinceLastPeriod.Should().Be(7.5m);
        summary.FinalPayRequired.Should().BeTrue();

        await new MarkFinalPayProcessedHandler(_h.Db, _h.Clock.Object).Handle(
            new MarkFinalPayProcessedCommand(worker.Id, EmployeeKinds.User, _adminId, true), CancellationToken.None);

        _h.Db.LeaveBalances.Single(b => b.UserId == worker.Id).Balance.Should().Be(0m);
        (await _h.Db.EmployeeProfiles.SingleAsync(p => p.UserId == worker.Id)).FinalPayProcessedAt.Should().Be(_h.Now);
    }
}
