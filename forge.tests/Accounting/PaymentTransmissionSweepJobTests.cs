using FluentAssertions;
using Hangfire;
using HangfireJob = Hangfire.Common.Job;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Self-healing sweep for stuck payment transmissions: re-enqueues Queued rows never attempted within
/// 10 minutes of creation and Retrying rows whose NextAttemptAt slot passed more than 10 minutes ago
/// (a lost Hangfire schedule), capped at 50 per run. Fresh rows and terminal states are left alone —
/// the worker job's own status checks make any double-enqueue harmless.
/// </summary>
public class PaymentTransmissionSweepJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static PaymentTransmissionSweepJob BuildJob(AppDbContext db, Mock<IBackgroundJobClient> jobs)
        => new(db, jobs.Object, new FixedClock(Now), NullLogger<PaymentTransmissionSweepJob>.Instance);

    private static Mock<IBackgroundJobClient> CapturingClient(List<HangfireJob> captured)
    {
        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.Create(It.IsAny<HangfireJob>(), It.IsAny<IState>()))
            .Callback<HangfireJob, IState>((job, state) =>
            {
                state.Should().BeOfType<EnqueuedState>();
                captured.Add(job);
            })
            .Returns("job-1");
        return jobs;
    }

    private static async Task<PaymentTransmission> AddTransmissionAsync(
        AppDbContext db,
        PaymentTransmissionStatus status,
        int attemptCount = 0,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? nextAttemptAt = null)
    {
        var transmission = new PaymentTransmission
        {
            SourceType = "VendorPayment",
            SourceId = 1,
            Status = status,
            AttemptCount = attemptCount,
            NextAttemptAt = nextAttemptAt,
            Amount = 100m,
            Method = "BankTransfer",
            CreatedAt = createdAt ?? Now, // explicit CreatedAt survives SetTimestamps
        };
        db.PaymentTransmissions.Add(transmission);
        await db.SaveChangesAsync();
        return transmission;
    }

    [Fact]
    public async Task Sweep_StuckQueuedAndOverdueRetrying_AreReEnqueued()
    {
        var db = TestDbContextFactory.Create();
        var stuckQueued = await AddTransmissionAsync(
            db, PaymentTransmissionStatus.Queued, createdAt: Now.AddMinutes(-15));
        var overdueRetrying = await AddTransmissionAsync(
            db, PaymentTransmissionStatus.Retrying, attemptCount: 2, nextAttemptAt: Now.AddMinutes(-11));

        var captured = new List<HangfireJob>();
        await BuildJob(db, CapturingClient(captured)).SweepAsync(CancellationToken.None);

        captured.Should().HaveCount(2);
        captured.Select(j => (int)j.Args[0]).Should().BeEquivalentTo([stuckQueued.Id, overdueRetrying.Id]);
        captured.Should().OnlyContain(j => j.Method.Name == nameof(PaymentTransmissionJob.ProcessAsync));
    }

    [Fact]
    public async Task Sweep_FreshRows_AreLeftAlone()
    {
        var db = TestDbContextFactory.Create();
        // Queued but within the 10-minute grace window.
        await AddTransmissionAsync(db, PaymentTransmissionStatus.Queued, createdAt: Now.AddMinutes(-5));
        // Retrying with its backoff slot still in the future.
        await AddTransmissionAsync(
            db, PaymentTransmissionStatus.Retrying, attemptCount: 1, nextAttemptAt: Now.AddMinutes(3));
        // Retrying whose slot passed, but not by more than the threshold (Hangfire is just slow).
        await AddTransmissionAsync(
            db, PaymentTransmissionStatus.Retrying, attemptCount: 1, nextAttemptAt: Now.AddMinutes(-9));
        // Queued already attempted (the worker owns it — the failure path schedules its own retry).
        await AddTransmissionAsync(
            db, PaymentTransmissionStatus.Queued, attemptCount: 1, createdAt: Now.AddMinutes(-30));

        var captured = new List<HangfireJob>();
        await BuildJob(db, CapturingClient(captured)).SweepAsync(CancellationToken.None);

        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task Sweep_TerminalStates_AreLeftAlone()
    {
        var db = TestDbContextFactory.Create();
        await AddTransmissionAsync(db, PaymentTransmissionStatus.Succeeded, attemptCount: 1, createdAt: Now.AddHours(-2));
        await AddTransmissionAsync(db, PaymentTransmissionStatus.Failed, attemptCount: 5, createdAt: Now.AddHours(-2));
        await AddTransmissionAsync(db, PaymentTransmissionStatus.Cancelled, createdAt: Now.AddHours(-2));

        var captured = new List<HangfireJob>();
        await BuildJob(db, CapturingClient(captured)).SweepAsync(CancellationToken.None);

        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task Sweep_CapsAtFiftyPerRun()
    {
        var db = TestDbContextFactory.Create();
        for (var i = 0; i < PaymentTransmissionSweepJob.MaxPerSweep + 10; i++)
            await AddTransmissionAsync(db, PaymentTransmissionStatus.Queued, createdAt: Now.AddMinutes(-20));

        var captured = new List<HangfireJob>();
        await BuildJob(db, CapturingClient(captured)).SweepAsync(CancellationToken.None);

        captured.Should().HaveCount(PaymentTransmissionSweepJob.MaxPerSweep);
    }
}
