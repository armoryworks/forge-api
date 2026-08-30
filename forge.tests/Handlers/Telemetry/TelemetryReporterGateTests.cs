using System.Net;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Telemetry;

/// <summary>
/// The consent gate. Every test here asks the same question — did anything leave the
/// building? — because a monitoring feature that transmits without permission is
/// worse than no monitoring feature at all.
/// </summary>
public class TelemetryReporterGateTests
{
    private sealed class FixedClock : Forge.Core.Interfaces.IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
    }

    /// <summary>Records every outbound request so "nothing was sent" is checkable.</summary>
    private sealed class SpyHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Body { get; set; } = "{}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static async Task<(AppDbContext Db, SpyHandler Spy, TelemetryReporter Reporter)> BuildAsync(
        Dictionary<string, string> settings, string endpoint = "https://tuyere.armoryworks.com")
    {
        var db = TestDbContextFactory.Create();
        foreach (var (key, value) in settings)
            db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
        await db.SaveChangesAsync();

        var spy = new SpyHandler();
        var reporter = new TelemetryReporter(
            db,
            new HttpClient(spy),
            new NoopHealthCheckService(),
            Options.Create(new TelemetryOptions { Endpoint = endpoint }),
            new FixedClock(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TelemetryReporter>.Instance);

        return (db, spy, reporter);
    }

    private sealed class NoopHealthCheckService : HealthCheckService
    {
        public override Task<HealthReport> CheckHealthAsync(
            Func<HealthCheckRegistration, bool>? predicate, CancellationToken ct = default)
            => Task.FromResult(new HealthReport(
                new Dictionary<string, HealthReportEntry>
                {
                    ["postgresql"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null),
                },
                TimeSpan.Zero));
    }

    [Fact]
    public async Task NeverAsked_SendsNothing()
    {
        var (db, spy, reporter) = await BuildAsync([]);
        using var _ = db;

        await reporter.RunAsync();

        spy.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Declined_SendsNothing()
    {
        var (db, spy, reporter) = await BuildAsync(new()
        {
            [TelemetrySettingKeys.Enabled] = "false",
            [TelemetrySettingKeys.ConsentDecision] = "declined",
            [TelemetrySettingKeys.ConsentVersion] = TelemetryAgreement.Version,
        });
        using var _ = db;

        await reporter.RunAsync();

        spy.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EnabledButWithoutAnAcceptedAgreement_SendsNothing()
    {
        // Belt and braces: the switch alone is not consent. If some other code path
        // ever flips `enabled` without recording a decision, nothing may leave.
        var (db, spy, reporter) = await BuildAsync(new()
        {
            [TelemetrySettingKeys.Enabled] = "true",
        });
        using var _ = db;

        await reporter.RunAsync();

        spy.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DeclinedButSomehowStillSwitchedOn_SendsNothing()
    {
        // Isolates the consent-decision gate specifically. Declining sets enabled=false,
        // so this state shouldn't arise — but each gate has to hold on its own, or the
        // only thing standing between a decline and a transmission is a single flag
        // that some future code path sets for an unrelated reason.
        var (db, spy, reporter) = await BuildAsync(new()
        {
            [TelemetrySettingKeys.Enabled] = "true",
            [TelemetrySettingKeys.ConsentDecision] = "declined",
            [TelemetrySettingKeys.ConsentVersion] = TelemetryAgreement.Version,
        });
        using var _ = db;

        await reporter.RunAsync();

        spy.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SwitchedOnWithCurrentTermsButNoRecordedAnswer_SendsNothing()
    {
        // The other half: a version stamped with no decision behind it is not consent.
        var (db, spy, reporter) = await BuildAsync(new()
        {
            [TelemetrySettingKeys.Enabled] = "true",
            [TelemetrySettingKeys.ConsentVersion] = TelemetryAgreement.Version,
        });
        using var _ = db;

        await reporter.RunAsync();

        spy.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ConsentToSupersededTerms_SendsNothing()
    {
        // Agreeing to last year's terms is not agreement to this year's.
        var (db, spy, reporter) = await BuildAsync(new()
        {
            [TelemetrySettingKeys.Enabled] = "true",
            [TelemetrySettingKeys.ConsentDecision] = "accepted",
            [TelemetrySettingKeys.ConsentVersion] = "0-superseded",
        });
        using var _ = db;

        await reporter.RunAsync();

        spy.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NoEndpointConfigured_SendsNothing()
    {
        var (db, spy, reporter) = await BuildAsync(new()
        {
            [TelemetrySettingKeys.Enabled] = "true",
            [TelemetrySettingKeys.ConsentDecision] = "accepted",
            [TelemetrySettingKeys.ConsentVersion] = TelemetryAgreement.Version,
        }, endpoint: "");
        using var _ = db;

        await reporter.RunAsync();

        spy.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Accepted_EnrollsFirstAndOnlyEnrolls()
    {
        var (db, spy, reporter) = await BuildAsync(new()
        {
            [TelemetrySettingKeys.Enabled] = "true",
            [TelemetrySettingKeys.ConsentDecision] = "accepted",
            [TelemetrySettingKeys.ConsentVersion] = TelemetryAgreement.Version,
        });
        using var _ = db;
        spy.Body = """{"installId":"abc","status":"Pending","pendingToken":"pt-123"}""";

        await reporter.RunAsync();

        var request = spy.Requests.Should().ContainSingle().Subject;
        request.RequestUri!.AbsolutePath.Should().Be("/api/public/telemetry/enroll");
        // No heartbeat until an admin accepts.
        (await Setting(db, TelemetrySettingKeys.PendingToken)).Should().Be("pt-123");
        (await Setting(db, TelemetrySettingKeys.Token)).Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Accepted_WithAToken_Heartbeats()
    {
        var (db, spy, reporter) = await BuildAsync(new()
        {
            [TelemetrySettingKeys.Enabled] = "true",
            [TelemetrySettingKeys.ConsentDecision] = "accepted",
            [TelemetrySettingKeys.ConsentVersion] = TelemetryAgreement.Version,
            [TelemetrySettingKeys.Token] = "live-token",
        });
        using var _ = db;

        await reporter.RunAsync();

        var request = spy.Requests.Should().ContainSingle().Subject;
        request.RequestUri!.AbsolutePath.Should().Be("/api/public/telemetry/heartbeat");
        request.Headers.Authorization!.Parameter.Should().Be("live-token");
    }

    [Fact]
    public async Task ARevokedToken_IsDroppedSoTheNextCycleReEnrolls()
    {
        // Monitoring paused at the far end shouldn't need the customer to do anything;
        // their consent hasn't changed.
        var (db, spy, reporter) = await BuildAsync(new()
        {
            [TelemetrySettingKeys.Enabled] = "true",
            [TelemetrySettingKeys.ConsentDecision] = "accepted",
            [TelemetrySettingKeys.ConsentVersion] = TelemetryAgreement.Version,
            [TelemetrySettingKeys.Token] = "revoked",
        });
        using var _ = db;
        spy.Status = HttpStatusCode.Unauthorized;

        await reporter.RunAsync();

        (await Setting(db, TelemetrySettingKeys.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnreachableEndpoint_IsRecordedAndSwallowed()
    {
        // A vendor's monitoring being down must never surface as an error on a
        // customer's shop floor.
        var (db, spy, reporter) = await BuildAsync(new()
        {
            [TelemetrySettingKeys.Enabled] = "true",
            [TelemetrySettingKeys.ConsentDecision] = "accepted",
            [TelemetrySettingKeys.ConsentVersion] = TelemetryAgreement.Version,
            [TelemetrySettingKeys.Token] = "live-token",
        });
        using var _ = db;
        spy.Status = HttpStatusCode.InternalServerError;

        var act = async () => await reporter.RunAsync();

        await act.Should().NotThrowAsync();
        (await Setting(db, TelemetrySettingKeys.LastError)).Should().NotBeNullOrEmpty();
    }

    private static async Task<string?> Setting(AppDbContext db, string key) =>
        (await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key))?.Value;
}
