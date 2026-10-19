using FluentAssertions;
using Hangfire;
using HangfireJob = Hangfire.Common.Job;
using Hangfire.States;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.PaymentTransmissions;
using Forge.Api.Features.VendorPayments;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Banking;

/// <summary>
/// banking.wire.manual-attestation keeps a waiting wire away from the bank channel on every path:
/// creation parks it at AwaitingAttestation without a job, the stuck-transmission sweep ignores it, the
/// channel job refuses it (also a legacy Queued wire that logged awaiting-wire-attestation), and a manual
/// retry sends it back to attestation instead of the channel.
/// </summary>
public class WireAttestationChannelTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static ISettingsService Attestation(bool on)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetBoolAsync(BankingSettings.WireManualAttestationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(on);
        return settings.Object;
    }

    private static Mock<IBackgroundJobClient> CapturingClient(List<HangfireJob> captured)
    {
        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.Create(It.IsAny<HangfireJob>(), It.IsAny<IState>()))
            .Callback<HangfireJob, IState>((job, _) => captured.Add(job))
            .Returns("job-1");
        return jobs;
    }

    private static Mock<IBankPaymentService> Bank()
    {
        var bank = new Mock<IBankPaymentService>();
        bank.Setup(b => b.SubmitPaymentAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankSubmissionResult(true, "MOCK-WIRE-1", null));
        return bank;
    }

    private static PaymentTransmissionJob BuildJob(AppDbContext db, Mock<IBankPaymentService> bank)
        => new(db, bank.Object, new Mock<IBackgroundJobClient>().Object, new Mock<IMediator>().Object,
            new FixedClock(Now), NullLogger<PaymentTransmissionJob>.Instance);

    private static async Task<(AppDbContext Db, Vendor Vendor)> SeedVendorAsync()
    {
        var db = TestDbContextFactory.Create();
        db.CurrentUserId = 7;
        var vendor = new Vendor { CompanyName = "Delta Supply", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        return (db, vendor);
    }

    private static async Task<(AppDbContext Db, PaymentTransmission Transmission)> SeedWireAsync(
        PaymentTransmissionStatus status, bool loggedAwaiting)
    {
        var (db, vendor) = await SeedVendorAsync();
        var payment = new VendorPayment
        {
            PaymentNumber = "VPMT-WIRE-1", VendorId = vendor.Id,
            Method = PaymentMethod.Wire, Amount = 5000m, PaymentDate = Now,
        };
        db.VendorPayments.Add(payment);
        await db.SaveChangesAsync();
        var transmission = new PaymentTransmission
        {
            SourceType = "VendorPayment", SourceId = payment.Id, Status = status,
            Amount = payment.Amount, Method = "Wire", CreatedByUserId = 7,
            CreatedAt = Now.AddHours(-1),
        };
        db.PaymentTransmissions.Add(transmission);
        if (loggedAwaiting)
        {
            db.ActivityLogs.Add(new ActivityLog
            {
                EntityType = "VendorPayment", EntityId = payment.Id,
                Action = WireAttestationState.AwaitingActivityAction, Description = "awaiting",
            });
        }
        await db.SaveChangesAsync();
        return (db, transmission);
    }

    [Fact]
    public async Task Create_WireWithAttestationOn_AwaitsAttestationWithoutAJob()
    {
        var (db, vendor) = await SeedVendorAsync();
        var captured = new List<HangfireJob>();
        var handler = new CreateVendorPaymentHandler(
            new VendorPaymentRepository(db), new VendorRepository(db), new VendorBillRepository(db), db,
            backgroundJobs: CapturingClient(captured).Object, settings: Attestation(true));

        var result = await handler.Handle(
            new CreateVendorPaymentCommand(vendor.Id, "Wire", 100m, Now, "REF-W", null, null),
            CancellationToken.None);

        db.PaymentTransmissions.Single().Status.Should().Be(PaymentTransmissionStatus.AwaitingAttestation);
        result.TransmissionStatus.Should().Be("AwaitingAttestation");
        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task Sweep_IgnoresAWireAwaitingAttestation()
    {
        var (db, _) = await SeedWireAsync(PaymentTransmissionStatus.AwaitingAttestation, loggedAwaiting: true);
        var captured = new List<HangfireJob>();

        await new PaymentTransmissionSweepJob(db, CapturingClient(captured).Object, new FixedClock(Now),
            NullLogger<PaymentTransmissionSweepJob>.Instance).SweepAsync(CancellationToken.None);

        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task Process_WireAwaitingAttestation_IsNotSubmitted()
    {
        var (db, transmission) = await SeedWireAsync(PaymentTransmissionStatus.AwaitingAttestation, loggedAwaiting: true);
        var bank = Bank();

        await BuildJob(db, bank).ProcessAsync(transmission.Id, CancellationToken.None);

        bank.Verify(b => b.SubmitPaymentAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var row = await db.PaymentTransmissions.AsNoTracking().SingleAsync();
        row.Status.Should().Be(PaymentTransmissionStatus.AwaitingAttestation);
        row.AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task Process_LegacyQueuedWireThatLoggedAwaiting_IsParkedNotSubmitted()
    {
        var (db, transmission) = await SeedWireAsync(PaymentTransmissionStatus.Queued, loggedAwaiting: true);
        var bank = Bank();

        await BuildJob(db, bank).ProcessAsync(transmission.Id, CancellationToken.None);

        bank.Verify(b => b.SubmitPaymentAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var row = await db.PaymentTransmissions.AsNoTracking().SingleAsync();
        row.Status.Should().Be(PaymentTransmissionStatus.AwaitingAttestation);
        row.AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task Process_QueuedWireOnTheChannel_IsSubmitted()
    {
        var (db, transmission) = await SeedWireAsync(PaymentTransmissionStatus.Queued, loggedAwaiting: false);
        var bank = Bank();

        await BuildJob(db, bank).ProcessAsync(transmission.Id, CancellationToken.None);

        bank.Verify(b => b.SubmitPaymentAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        (await db.PaymentTransmissions.AsNoTracking().SingleAsync()).Status.Should().Be(PaymentTransmissionStatus.Succeeded);
    }

    [Theory]
    [InlineData(true, PaymentTransmissionStatus.AwaitingAttestation, 0)]
    [InlineData(false, PaymentTransmissionStatus.Queued, 1)]
    public async Task Retry_CancelledWire_FollowsTheAttestationSetting(
        bool attestationOn, PaymentTransmissionStatus expected, int expectedJobs)
    {
        var (db, transmission) = await SeedWireAsync(PaymentTransmissionStatus.Cancelled, loggedAwaiting: true);
        var captured = new List<HangfireJob>();

        var result = await new RetryPaymentTransmissionHandler(db, CapturingClient(captured).Object, Attestation(attestationOn))
            .Handle(new RetryPaymentTransmissionCommand(transmission.Id), CancellationToken.None);

        result.Status.Should().Be(expected.ToString());
        captured.Should().HaveCount(expectedJobs);
    }

    [Fact]
    public async Task Process_WireRequeuedByHandWithAttestationOff_IsSubmitted()
    {
        var (db, transmission) = await SeedWireAsync(PaymentTransmissionStatus.Cancelled, loggedAwaiting: true);
        await new RetryPaymentTransmissionHandler(db, null, Attestation(false))
            .Handle(new RetryPaymentTransmissionCommand(transmission.Id), CancellationToken.None);
        var bank = Bank();

        await BuildJob(db, bank).ProcessAsync(transmission.Id, CancellationToken.None);

        bank.Verify(b => b.SubmitPaymentAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
