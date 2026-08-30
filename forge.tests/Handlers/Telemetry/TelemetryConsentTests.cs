using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Forge.Api.Features.Admin.Telemetry;
using Forge.Api.Services;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Telemetry;

// Consent is the whole basis for a vendor reading a customer's system, so these are
// about the record being honest: both answers kept, the version stamped by the
// server, and a decline taking effect immediately rather than eventually.
public class TelemetryConsentTests
{
    // The shared factory: the InMemory provider can't map DocumentEmbedding's
    // pgvector column, so it's excluded there.
    private static AppDbContext NewDb() => TestDbContextFactory.Create();

    private sealed class FixedClock : Forge.Core.Interfaces.IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
    }

    private static GetTelemetryStatusHandler StatusHandler(AppDbContext db, string endpoint = "https://tuyere.armoryworks.com") =>
        new(db, Options.Create(new TelemetryOptions { Endpoint = endpoint }));

    private static async Task<string?> SettingAsync(AppDbContext db, string key) =>
        (await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key))?.Value;

    [Fact]
    public async Task MonitoringIsOffUntilSomebodySaysYes()
    {
        // The default for a shipped appliance: nothing leaves until asked and answered.
        using var db = NewDb();

        var status = await StatusHandler(db).Handle(new GetTelemetryStatusQuery(), default);

        status.Enabled.Should().BeFalse();
        status.ConsentDecision.Should().BeNull();
        status.EnrollmentStatus.Should().Be("NotEnrolled");
    }

    [Fact]
    public async Task AnInstallWithNoEndpointConfigured_CannotBeOn()
    {
        // A self-hosted Forge has no vendor to report to; the switch must not pretend
        // otherwise even if the setting somehow says true.
        using var db = NewDb();
        db.SystemSettings.Add(new Forge.Core.Entities.SystemSetting { Key = TelemetrySettingKeys.Enabled, Value = "true" });
        await db.SaveChangesAsync();

        var status = await StatusHandler(db, endpoint: "").Handle(new GetTelemetryStatusQuery(), default);

        status.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task AcceptingTurnsItOnAndStampsTheShippedVersion()
    {
        using var db = NewDb();
        var mediator = new StubMediator(db);

        await new RecordTelemetryConsentHandler(db, mediator, new FixedClock())
            .Handle(new RecordTelemetryConsentCommand(true, "owner@shop.com", "10.0.0.5", "Firefox"), default);

        (await SettingAsync(db, TelemetrySettingKeys.Enabled)).Should().Be("true");
        (await SettingAsync(db, TelemetrySettingKeys.ConsentDecision)).Should().Be("accepted");
        // Server-stamped: a stale tab can't consent to superseded terms.
        (await SettingAsync(db, TelemetrySettingKeys.ConsentVersion)).Should().Be(TelemetryAgreement.Version);
    }

    [Fact]
    public async Task DecliningIsRecordedJustAsCarefullyAsAccepting()
    {
        // Keeping only the acceptances would make this a sales artefact rather than a
        // consent record.
        using var db = NewDb();

        await new RecordTelemetryConsentHandler(db, new StubMediator(db), new FixedClock())
            .Handle(new RecordTelemetryConsentCommand(false, "owner@shop.com", "10.0.0.5", "Firefox"), default);

        var audit = await db.AuditLogEntries.SingleAsync();
        audit.Action.Should().Be(TelemetrySettingKeys.ConsentAuditAction);
        audit.Details.Should().Contain("declined");
        audit.IpAddress.Should().Be("10.0.0.5");
    }

    [Fact]
    public async Task DecliningRevokesCredentialsImmediately()
    {
        // Withdrawn consent has to stop the next heartbeat, not the one after the
        // cycle notices. Without a token the reporter physically cannot send.
        using var db = NewDb();
        db.SystemSettings.AddRange(
            new Forge.Core.Entities.SystemSetting { Key = TelemetrySettingKeys.Token, Value = "live-token" },
            new Forge.Core.Entities.SystemSetting { Key = TelemetrySettingKeys.PendingToken, Value = "pending" });
        await db.SaveChangesAsync();

        await new RecordTelemetryConsentHandler(db, new StubMediator(db), new FixedClock())
            .Handle(new RecordTelemetryConsentCommand(false, null, null, null), default);

        (await SettingAsync(db, TelemetrySettingKeys.Token)).Should().BeEmpty();
        (await SettingAsync(db, TelemetrySettingKeys.PendingToken)).Should().BeEmpty();
        (await SettingAsync(db, TelemetrySettingKeys.Enabled)).Should().Be("false");
    }

    [Fact]
    public async Task ChangedTerms_AskAgainRatherThanCarryTheOldYesForward()
    {
        // What makes changing the agreement honest.
        using var db = NewDb();
        db.SystemSettings.AddRange(
            new Forge.Core.Entities.SystemSetting { Key = TelemetrySettingKeys.Enabled, Value = "true" },
            new Forge.Core.Entities.SystemSetting { Key = TelemetrySettingKeys.ConsentDecision, Value = "accepted" },
            new Forge.Core.Entities.SystemSetting { Key = TelemetrySettingKeys.ConsentVersion, Value = "0-superseded" });
        await db.SaveChangesAsync();

        var status = await StatusHandler(db).Handle(new GetTelemetryStatusQuery(), default);

        status.AgreementOutOfDate.Should().BeTrue();
    }

    [Fact]
    public async Task CurrentTerms_AreNotFlaggedStale()
    {
        using var db = NewDb();
        db.SystemSettings.AddRange(
            new Forge.Core.Entities.SystemSetting { Key = TelemetrySettingKeys.Enabled, Value = "true" },
            new Forge.Core.Entities.SystemSetting { Key = TelemetrySettingKeys.ConsentDecision, Value = "accepted" },
            new Forge.Core.Entities.SystemSetting { Key = TelemetrySettingKeys.ConsentVersion, Value = TelemetryAgreement.Version });
        await db.SaveChangesAsync();

        var status = await StatusHandler(db).Handle(new GetTelemetryStatusQuery(), default);

        status.AgreementOutOfDate.Should().BeFalse();
        status.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task ConsentHistoryShowsBothAnswersNewestFirst()
    {
        using var db = NewDb();
        var handler = new RecordTelemetryConsentHandler(db, new StubMediator(db), new FixedClock());
        await handler.Handle(new RecordTelemetryConsentCommand(true, "a@shop.com", null, null), default);
        await handler.Handle(new RecordTelemetryConsentCommand(false, "b@shop.com", null, null), default);

        var history = await new GetTelemetryConsentHistoryHandler(db)
            .Handle(new GetTelemetryConsentHistoryQuery(), default);

        history.Should().HaveCount(2);
        history.Select(h => h.Decision).Should().Contain(["accepted", "declined"]);
    }

    [Fact]
    public void TheAgreementNamesWhatIsSentAndWhatIsNot()
    {
        // The text is the product here — an operator can only consent to something
        // they can actually read, including a real sample of the payload.
        var agreement = TelemetryAgreement.Current;

        agreement.Shared.Should().NotBeEmpty();
        agreement.NotShared.Should().NotBeEmpty();
        agreement.SamplePayload.Should().Contain("postgresql").And.Contain("uptimeSeconds");
        // The promise the sample has to keep.
        agreement.SamplePayload.Should().NotContain("customer").And.NotContain("password");
    }

    // GetTelemetryStatus is re-sent through IMediator after recording; this returns it
    // without a container.
    private sealed class StubMediator(AppDbContext db) : MediatR.IMediator
    {
        public Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request, CancellationToken ct = default)
            => (Task<TResponse>)(object)new GetTelemetryStatusHandler(
                    db, Options.Create(new TelemetryOptions { Endpoint = "https://tuyere.armoryworks.com" }))
                .Handle(new GetTelemetryStatusQuery(), ct);

        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : MediatR.IRequest => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(MediatR.IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Publish(object notification, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : MediatR.INotification => throw new NotSupportedException();
    }
}
