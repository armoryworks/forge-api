using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Forge.Api.Services;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin.Telemetry;

/// <summary>
/// What the settings screen renders: whether monitoring is on, how far the enrollment
/// has got, and who last decided.
///
/// Reports the agreement as out of date when the shipped version has moved past the
/// one that was accepted. That is what makes changing the terms honest — a customer
/// who agreed to health-only reporting is asked again rather than silently carried
/// onto whatever the terms say now.
/// </summary>
public record GetTelemetryStatusQuery : IRequest<TelemetryStatusResponseModel>;

public class GetTelemetryStatusHandler(AppDbContext db, IOptions<TelemetryOptions> options)
    : IRequestHandler<GetTelemetryStatusQuery, TelemetryStatusResponseModel>
{
    public async Task<TelemetryStatusResponseModel> Handle(GetTelemetryStatusQuery request, CancellationToken ct)
    {
        var settings = await db.SystemSettings
            .Where(s => s.Key.StartsWith("telemetry."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var consentVersion = Get(settings, TelemetrySettingKeys.ConsentVersion);
        var decision = Get(settings, TelemetrySettingKeys.ConsentDecision);

        // An install with no endpoint configured has no vendor to report to — the
        // screen says unavailable rather than offering a switch that does nothing.
        var enabled = string.Equals(Get(settings, TelemetrySettingKeys.Enabled), "true", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(options.Value.Endpoint);

        return new TelemetryStatusResponseModel(
            Enabled: enabled,
            EnrollmentStatus: Get(settings, TelemetrySettingKeys.EnrollmentStatus) ?? "NotEnrolled",
            ConsentVersion: consentVersion,
            ConsentDecision: decision,
            ConsentAt: ParseDate(Get(settings, TelemetrySettingKeys.ConsentAt)),
            ConsentBy: Get(settings, TelemetrySettingKeys.ConsentBy),
            LastHeartbeatAt: ParseDate(Get(settings, TelemetrySettingKeys.LastHeartbeatAt)),
            LastError: Get(settings, TelemetrySettingKeys.LastError),
            AgreementOutOfDate: decision == "accepted" && consentVersion != TelemetryAgreement.Version);
    }

    private static string? Get(Dictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}
