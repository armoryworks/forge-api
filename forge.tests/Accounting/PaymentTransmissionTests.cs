using FluentAssertions;
using Hangfire;
using HangfireJob = Hangfire.Common.Job;
using Hangfire.States;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Notifications;
using Forge.Api.Features.PaymentTransmissions;
using Forge.Api.Features.VendorPayments;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Failed-transaction handling for electronic vendor payments: the <see cref="PaymentTransmissionJob"/>
/// retry/backoff cycle (1 initial attempt + 4 retries at 1/4/16/64 min), the terminal Failed → critical
/// notification path, the enqueue-on-create wiring in <see cref="CreateVendorPaymentHandler"/>, the
/// manual-retry handler, and the latest-transmission projections on the AP list models. Hangfire is NOT
/// running here — the job is instantiated directly and <see cref="IBackgroundJobClient"/> is mocked to
/// capture Enqueue/Schedule (both funnel into <c>Create(Job, IState)</c>).
/// </summary>
public class PaymentTransmissionTests
{
    private const int CreatorUserId = 7;

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(AppDbContext db, VendorPayment payment)> SeedPaymentAsync(
        string? referenceNumber = null, PaymentMethod method = PaymentMethod.BankTransfer)
    {
        var db = TestDbContextFactory.Create();
        var vendor = new Vendor { CompanyName = "Delta Supply", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();

        var payment = new VendorPayment
        {
            PaymentNumber = "VPMT-00001",
            VendorId = vendor.Id,
            Method = method,
            Amount = 250m,
            PaymentDate = Now,
            ReferenceNumber = referenceNumber,
        };
        db.VendorPayments.Add(payment);
        await db.SaveChangesAsync();
        return (db, payment);
    }

    private static async Task<PaymentTransmission> SeedTransmissionAsync(
        AppDbContext db, VendorPayment payment,
        PaymentTransmissionStatus status = PaymentTransmissionStatus.Queued,
        int attemptCount = 0)
    {
        var transmission = new PaymentTransmission
        {
            SourceType = "VendorPayment",
            SourceId = payment.Id,
            Status = status,
            AttemptCount = attemptCount,
            Amount = payment.Amount,
            Method = payment.Method.ToString(),
            CreatedByUserId = CreatorUserId,
        };
        db.PaymentTransmissions.Add(transmission);
        await db.SaveChangesAsync();
        return transmission;
    }

    private static PaymentTransmissionJob BuildJob(
        AppDbContext db,
        Mock<IBankPaymentService> bank,
        Mock<IBackgroundJobClient> jobs,
        Mock<IMediator>? mediator = null)
        => new(db, bank.Object, jobs.Object, (mediator ?? new Mock<IMediator>()).Object,
            new FixedClock(Now), NullLogger<PaymentTransmissionJob>.Instance);

    private static Mock<IBankPaymentService> BankReturning(BankSubmissionResult result)
    {
        var bank = new Mock<IBankPaymentService>();
        bank.Setup(b => b.SubmitPaymentAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return bank;
    }

    // ── Job: success on first attempt ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Process_SuccessOnFirstAttempt_MarksSucceededWithRefAndActivity()
    {
        var (db, payment) = await SeedPaymentAsync(referenceNumber: "REF-1");
        var transmission = await SeedTransmissionAsync(db, payment);

        var bank = BankReturning(new BankSubmissionResult(true, "MOCK-ACH-VendorPayment-1", null));
        var jobs = new Mock<IBackgroundJobClient>();

        await BuildJob(db, bank, jobs).ProcessAsync(transmission.Id, CancellationToken.None);

        transmission.Status.Should().Be(PaymentTransmissionStatus.Succeeded);
        transmission.SubmissionRef.Should().Be("MOCK-ACH-VendorPayment-1");
        transmission.AttemptCount.Should().Be(1);
        transmission.LastAttemptAt.Should().Be(Now);
        transmission.NextAttemptAt.Should().BeNull();
        transmission.LastError.Should().BeNull();

        db.ActivityLogs.Should().Contain(a =>
            a.EntityType == "VendorPayment" && a.EntityId == payment.Id && a.Action == "transmission-succeeded");

        // No retry scheduled on success.
        jobs.Verify(j => j.Create(It.IsAny<HangfireJob>(), It.IsAny<IState>()), Times.Never);
    }

    // ── Job: transient failure → Retrying + scheduled backoff ──────────────────────────────────

    [Fact]
    public async Task Process_FirstFailure_SetsRetryingAndSchedulesOneMinuteBackoff()
    {
        var (db, payment) = await SeedPaymentAsync();
        var transmission = await SeedTransmissionAsync(db, payment);

        var bank = BankReturning(new BankSubmissionResult(false, null, "Mock bank API unavailable"));
        var jobs = new Mock<IBackgroundJobClient>();
        IState? capturedState = null;
        jobs.Setup(j => j.Create(It.IsAny<HangfireJob>(), It.IsAny<IState>()))
            .Callback<HangfireJob, IState>((_, state) => capturedState = state)
            .Returns("job-1");

        await BuildJob(db, bank, jobs).ProcessAsync(transmission.Id, CancellationToken.None);

        transmission.Status.Should().Be(PaymentTransmissionStatus.Retrying);
        transmission.AttemptCount.Should().Be(1);
        transmission.LastError.Should().Be("Mock bank API unavailable");
        transmission.NextAttemptAt.Should().Be(Now.AddMinutes(1));

        capturedState.Should().BeOfType<ScheduledState>();
        ((ScheduledState)capturedState!).EnqueueAt
            .Should().BeCloseTo(DateTime.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 4)]
    [InlineData(3, 16)]
    [InlineData(4, 64)]
    public void DelayForAttempt_IsExponentialTimesFour(int attemptCount, int expectedMinutes)
        => PaymentTransmissionJob.DelayForAttempt(attemptCount)
            .Should().Be(TimeSpan.FromMinutes(expectedMinutes));

    // ── Job: final failure → Failed + critical notification, no further schedule ───────────────

    [Fact]
    public async Task Process_FailureOnFinalAttempt_MarksFailedNotifiesCreatorAndStopsRetrying()
    {
        var (db, payment) = await SeedPaymentAsync();
        var transmission = await SeedTransmissionAsync(
            db, payment, PaymentTransmissionStatus.Retrying, attemptCount: 4);

        var bank = BankReturning(new BankSubmissionResult(false, null, "Mock bank API unavailable"));
        var jobs = new Mock<IBackgroundJobClient>();
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationResponseModel(
                1, "alert", "critical", "payment-transmission-failed", "t", "m",
                false, false, false, null, null, null, null, Now));

        await BuildJob(db, bank, jobs, mediator).ProcessAsync(transmission.Id, CancellationToken.None);

        transmission.Status.Should().Be(PaymentTransmissionStatus.Failed);
        transmission.AttemptCount.Should().Be(PaymentTransmissionJob.MaxAttempts);
        transmission.NextAttemptAt.Should().BeNull();
        transmission.LastError.Should().Be("Mock bank API unavailable");

        db.ActivityLogs.Should().Contain(a =>
            a.EntityType == "VendorPayment" && a.EntityId == payment.Id && a.Action == "transmission-failed");

        mediator.Verify(m => m.Send(
            It.Is<CreateNotificationCommand>(c =>
                c.Data.UserId == CreatorUserId
                && c.Data.Severity == "critical"
                && c.Data.EntityType == "VendorPayment"
                && c.Data.EntityId == payment.Id
                && c.Data.Message.Contains(payment.PaymentNumber)),
            It.IsAny<CancellationToken>()), Times.Once);

        // Terminal — no further retry scheduled.
        jobs.Verify(j => j.Create(It.IsAny<HangfireJob>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task Process_TerminalTransmission_IsSkipped()
    {
        var (db, payment) = await SeedPaymentAsync();
        var transmission = await SeedTransmissionAsync(
            db, payment, PaymentTransmissionStatus.Succeeded, attemptCount: 1);

        var bank = new Mock<IBankPaymentService>();
        await BuildJob(db, bank, new Mock<IBackgroundJobClient>())
            .ProcessAsync(transmission.Id, CancellationToken.None);

        transmission.AttemptCount.Should().Be(1);
        bank.Verify(b => b.SubmitPaymentAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── CreateVendorPayment: electronic methods queue a transmission, others don't ─────────────

    [Fact]
    public async Task CreateVendorPayment_BankTransfer_CreatesTransmissionAndEnqueuesJob()
    {
        var db = TestDbContextFactory.Create();
        db.CurrentUserId = CreatorUserId;
        var vendor = new Vendor { CompanyName = "Delta Supply", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();

        var jobs = new Mock<IBackgroundJobClient>();
        IState? capturedState = null;
        jobs.Setup(j => j.Create(It.IsAny<HangfireJob>(), It.IsAny<IState>()))
            .Callback<HangfireJob, IState>((_, state) => capturedState = state)
            .Returns("job-1");

        var handler = new CreateVendorPaymentHandler(
            new VendorPaymentRepository(db), new VendorRepository(db), new VendorBillRepository(db), db,
            backgroundJobs: jobs.Object);

        var result = await handler.Handle(
            new CreateVendorPaymentCommand(vendor.Id, "BankTransfer", 100m, Now, "REF-9", null, null),
            CancellationToken.None);

        var transmission = db.PaymentTransmissions.Single();
        transmission.SourceType.Should().Be("VendorPayment");
        transmission.SourceId.Should().Be(result.Id);
        transmission.Status.Should().Be(PaymentTransmissionStatus.Queued);
        transmission.Amount.Should().Be(100m);
        transmission.Method.Should().Be("BankTransfer");
        transmission.CreatedByUserId.Should().Be(CreatorUserId);

        result.TransmissionStatus.Should().Be("Queued");
        result.TransmissionId.Should().Be(transmission.Id);

        capturedState.Should().BeOfType<EnqueuedState>();
        db.ActivityLogs.Should().Contain(a =>
            a.EntityType == "VendorPayment" && a.EntityId == result.Id && a.Action == "transmission-queued");
    }

    [Fact]
    public async Task CreateVendorPayment_Check_CreatesNoTransmission()
    {
        var db = TestDbContextFactory.Create();
        var vendor = new Vendor { CompanyName = "Delta Supply", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();

        var jobs = new Mock<IBackgroundJobClient>();
        var handler = new CreateVendorPaymentHandler(
            new VendorPaymentRepository(db), new VendorRepository(db), new VendorBillRepository(db), db,
            backgroundJobs: jobs.Object);

        var result = await handler.Handle(
            new CreateVendorPaymentCommand(vendor.Id, "Check", 100m, Now, null, null, null),
            CancellationToken.None);

        db.PaymentTransmissions.Should().BeEmpty();
        result.TransmissionStatus.Should().BeNull();
        result.TransmissionId.Should().BeNull();
        jobs.Verify(j => j.Create(It.IsAny<HangfireJob>(), It.IsAny<IState>()), Times.Never);
    }

    // ── Manual retry handler ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retry_FailedTransmission_RequeuesWithFreshAttemptCycle()
    {
        var (db, payment) = await SeedPaymentAsync();
        var transmission = await SeedTransmissionAsync(
            db, payment, PaymentTransmissionStatus.Failed, attemptCount: 5);
        transmission.LastError = "Mock bank API unavailable";
        await db.SaveChangesAsync();

        var jobs = new Mock<IBackgroundJobClient>();
        IState? capturedState = null;
        jobs.Setup(j => j.Create(It.IsAny<HangfireJob>(), It.IsAny<IState>()))
            .Callback<HangfireJob, IState>((_, state) => capturedState = state)
            .Returns("job-1");

        var result = await new RetryPaymentTransmissionHandler(db, jobs.Object)
            .Handle(new RetryPaymentTransmissionCommand(transmission.Id), CancellationToken.None);

        transmission.Status.Should().Be(PaymentTransmissionStatus.Queued);
        transmission.AttemptCount.Should().Be(0);       // fresh 5-attempt cycle
        transmission.NextAttemptAt.Should().BeNull();
        transmission.LastError.Should().Be("Mock bank API unavailable"); // kept until next attempt

        result.Status.Should().Be("Queued");
        result.MaxAttempts.Should().Be(PaymentTransmissionJob.MaxAttempts);
        capturedState.Should().BeOfType<EnqueuedState>();
        db.ActivityLogs.Should().Contain(a =>
            a.EntityType == "VendorPayment" && a.EntityId == payment.Id && a.Action == "transmission-retried");
    }

    [Fact]
    public async Task Retry_SucceededTransmission_Throws()
    {
        var (db, payment) = await SeedPaymentAsync();
        var transmission = await SeedTransmissionAsync(
            db, payment, PaymentTransmissionStatus.Succeeded, attemptCount: 1);

        var act = () => new RetryPaymentTransmissionHandler(db)
            .Handle(new RetryPaymentTransmissionCommand(transmission.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── AP list-model projections ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task VendorPaymentList_CarriesLatestTransmissionStatus()
    {
        var (db, payment) = await SeedPaymentAsync();
        await SeedTransmissionAsync(db, payment, PaymentTransmissionStatus.Failed, attemptCount: 5);

        var items = await new VendorPaymentRepository(db).GetAllAsync(null, CancellationToken.None);

        var item = items.Single(p => p.Id == payment.Id);
        item.TransmissionStatus.Should().Be("Failed");
        item.TransmissionAttempts.Should().Be(5);
        item.TransmissionId.Should().NotBeNull();
    }

    [Fact]
    public async Task VendorBillList_FlagsBillPaidByFailedTransmissionPayment()
    {
        var (db, payment) = await SeedPaymentAsync();
        await SeedTransmissionAsync(db, payment, PaymentTransmissionStatus.Failed, attemptCount: 5);

        var bill = new VendorBill
        {
            BillNumber = "BILL-00001",
            VendorId = payment.VendorId,
            Status = VendorBillStatus.Paid,
            BillDate = Now,
            DueDate = Now.AddDays(30),
        };
        bill.PaymentApplications.Add(new VendorPaymentApplication
        {
            VendorPaymentId = payment.Id,
            Amount = payment.Amount,
        });
        db.VendorBills.Add(bill);
        await db.SaveChangesAsync();

        var items = await new VendorBillRepository(db).GetAllAsync(null, null, CancellationToken.None);

        items.Single(b => b.Id == bill.Id).HasFailedTransmission.Should().BeTrue();
    }

    [Fact]
    public async Task VendorBillList_NoFlagWhenLatestTransmissionSucceeded()
    {
        var (db, payment) = await SeedPaymentAsync();
        // Older Failed transmission superseded by a later Succeeded one (e.g. manual retry worked).
        await SeedTransmissionAsync(db, payment, PaymentTransmissionStatus.Failed, attemptCount: 5);
        await SeedTransmissionAsync(db, payment, PaymentTransmissionStatus.Succeeded, attemptCount: 1);

        var bill = new VendorBill
        {
            BillNumber = "BILL-00002",
            VendorId = payment.VendorId,
            Status = VendorBillStatus.Paid,
            BillDate = Now,
            DueDate = Now.AddDays(30),
        };
        bill.PaymentApplications.Add(new VendorPaymentApplication
        {
            VendorPaymentId = payment.Id,
            Amount = payment.Amount,
        });
        db.VendorBills.Add(bill);
        await db.SaveChangesAsync();

        var items = await new VendorBillRepository(db).GetAllAsync(null, null, CancellationToken.None);

        items.Single(b => b.Id == bill.Id).HasFailedTransmission.Should().BeFalse();
    }
}
